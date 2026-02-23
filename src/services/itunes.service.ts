import axios from 'axios';
import CircuitBreaker from 'opossum';
import { logger } from '../utils/logger.js';
import { ItunesResponseSchema, ItunesTrackSchema, DeezerResponseSchema } from '../schemas/media.schema.js';
import { z } from 'zod';

export interface TrackContext {
    art: string | null;
    url: string;
}

const trackCache = new Map<string, TrackContext>();

const itunesClient = axios.create({
    baseURL: 'https://itunes.apple.com',
    timeout: 5000,
});

const deezerClient = axios.create({
    baseURL: 'https://api.deezer.com',
    timeout: 5000,
});

const cbOptions = {
    timeout: 5000,
    errorThresholdPercentage: 50,
    resetTimeout: 30000
};

async function executeItunesSearch(url: string): Promise<z.infer<typeof ItunesResponseSchema>> {
    const response = await itunesClient.get(url);
    return ItunesResponseSchema.parse(response.data);
}

const breaker = new CircuitBreaker(executeItunesSearch, cbOptions);

breaker.fallback(() => {
    logger.warn('[CircuitBreaker] Mode dégradé activé. L\'API iTunes est temporairement ignorée.');
    return { resultCount: 0, results: [] };
});

breaker.on('open', () => logger.warn('[CircuitBreaker] Circuit ouvert (Echecs répétés)'));
breaker.on('close', () => logger.info('[CircuitBreaker] Circuit refermé (Service rétabli)'));

function sanitizeSearchTerm(term: string): string {
    if (!term) return '';
    return term
        .replace(/\s-\sSingle$/i, '')
        .replace(/\s-\sEP$/i, '')
        .replace(/\(feat\..*?\)/i, '')
        .replace(/\[.*?\]/g, '')
        .replace(/\sfeat\.\s.*/i, '')
        .replace(/\sft\.\s.*/i, '')
        .trim();
}

function extractMainArtist(artist: string): string {
    if (!artist) return '';
    // Retire les strings parasites ajoutées par Apple Music Windows ("Artiste — Album")
    return artist.split(' — ')[0].split(' - ')[0].split(',')[0].split(' & ')[0].split(' feat ')[0].split(' ft ')[0].trim();
}

export async function resolveTrackInfo(artist: string, album: string, title: string): Promise<TrackContext | null> {
    const mainArtist = extractMainArtist(artist);
    const cleanTitle = sanitizeSearchTerm(title);

    const cacheKey = `${mainArtist}-${cleanTitle}`;
    if (trackCache.has(cacheKey)) {
        return trackCache.get(cacheKey) || null;
    }

    try {
        const query = encodeURIComponent(`${mainArtist} ${cleanTitle}`);
        const url = `/search?term=${query}&media=music&entity=song&limit=1&country=FR`;

        const data = await breaker.fire(url) as z.infer<typeof ItunesResponseSchema>;

        if (data.resultCount > 0 && data.results && data.results.length > 0) {
            const match = data.results[0];
            let artUrl = match.artworkUrl100 || null;
            if (artUrl) artUrl = artUrl.replace('100x100bb', '1024x1024bb');

            const info: TrackContext = { art: artUrl, url: match.trackViewUrl || 'https://music.apple.com/' };

            // Prevent Memory Leak: LRU Cache logic simplified (max 500 records)
            if (trackCache.size > 500) {
                const firstKey = trackCache.keys().next().value;
                if (firstKey) trackCache.delete(firstKey);
            }

            trackCache.set(cacheKey, info);
            return info;
        }
    } catch (error: any) {
        logger.error(`[API iTunes] Echec de résolution pour ${cacheKey} : ${error.message || 'Erreur inconnue'}`);
    }

    // Fallback 1: Recherche sur Deezer API (Gratuit, Open, Sans Auth)
    try {
        const deezerQuery = encodeURIComponent(`artist:"${mainArtist}" track:"${cleanTitle}"`);
        const urlDeezer = `/search?q=${deezerQuery}&limit=1`;

        const responseDeezer = await deezerClient.get(urlDeezer);
        const dataDeezer = DeezerResponseSchema.parse(responseDeezer.data);

        if (dataDeezer.data && dataDeezer.data.length > 0) {
            const matchDeezer = dataDeezer.data[0];
            let artUrl = matchDeezer.album?.cover_xl || null;

            if (artUrl) {
                const infoDeezer: TrackContext = { art: artUrl, url: matchDeezer.link || 'https://music.apple.com/' };

                if (trackCache.size > 500) {
                    const firstKey = trackCache.keys().next().value;
                    if (firstKey) trackCache.delete(firstKey);
                }

                trackCache.set(cacheKey, infoDeezer);
                logger.info(`[Fallback Deezer] Pochette trouvée pour ${cacheKey}`);
                return infoDeezer;
            }
        }
    } catch (error: any) {
        logger.warn(`[Fallback Deezer] Echec pour ${cacheKey}`);
    }

    // Fallback 2: UI-Avatars
    const fallbackText = `${cleanTitle.charAt(0)}${mainArtist ? mainArtist.charAt(0) : ''}`.toUpperCase();
    const fallbackArt = `https://ui-avatars.com/api/?name=${encodeURIComponent(fallbackText)}&background=fa233b&color=fff&size=512&font-size=0.45&bold=true`;

    const fallbackInfo: TrackContext = {
        art: fallbackArt,
        url: 'https://music.apple.com/'
    };

    trackCache.set(cacheKey, fallbackInfo);
    return fallbackInfo;
}
