import { spawn, ChildProcessWithoutNullStreams } from 'child_process';
import { Client } from 'discord-rpc';
import * as path from 'path';
import * as readline from 'readline';
import { logger } from './utils/logger';
import { MediaPayloadSchema, MediaPayload } from './schemas/media.schema';
import { resolveTrackInfo } from './services/itunes.service';

const CLIENT_ID = '1114806909590048798';
const rpcClient = new Client({ transport: 'ipc' });

let extractorProcess: import('child_process').ChildProcess | null = null;
let isRpcReady = false;

function sanitizeString(str: string): string {
    return (str || '').replace(/[\x00-\x1F\x7F]/g, '').trim();
}

async function updateDiscordActivity(payload: MediaPayload) {
    if (!isRpcReady) return;

    if (payload.status === 'Stopped' || payload.status === 'Paused' || payload.status === 'Error') {
        logger.debug('[Discord RPC] Nettoyage de l\'activité (Arrêt/Pause/Erreur)');
        rpcClient.clearActivity().catch(err => logger.error({ err }, '[RPC] Echec clearActivity'));
        return;
    }

    const title = sanitizeString(payload.title);
    let artist = sanitizeString(payload.artist);
    const album = sanitizeString(payload.album);

    if (title.length < 2) return;
    if (artist.length < 2) artist = 'Unknown Artist';

    logger.info(`[Lecture en cours] ${artist} - ${title} [${payload.status}]`);

    const trackInfo = await resolveTrackInfo(artist, album, title);
    const albumArt = trackInfo?.art || 'apple_music_logo';
    const trackUrl = trackInfo?.url || 'https://music.apple.com/';

    const activity: any = {
        details: title,
        state: artist,
        assets: {
            large_image: albumArt,
            large_text: album || 'Apple Music'
        },
        buttons: [
            { label: 'Écouter sur Apple Music', url: trackUrl }
        ],
        type: 2,
        instance: false
    };

    if (payload.status === 'Playing' && payload.startTime && payload.endTime) {
        activity.timestamps = {
            start: payload.startTime,
            end: payload.endTime
        };
    }

    (rpcClient as any).request('SET_ACTIVITY', {
        pid: process.pid,
        activity: activity
    }).catch((err: any) => logger.error({ err }, '[RPC] Erreur SET_ACTIVITY'));
}

function startNativeExtractor() {
    const isPkg = typeof (process as any).pkg !== 'undefined';
    const exePath = isPkg
        ? path.join(path.dirname(process.execPath), 'MediaExtractor', 'MediaExtractor.exe')
        : path.join(__dirname, '../src/MediaExtractor/MediaExtractor.exe');
    logger.info(`[Extractor] Démarrage du binaire C# (${exePath})`);

    extractorProcess = spawn(exePath, [], {
        windowsHide: true,
        stdio: ['ignore', 'pipe', 'pipe']
    });

    if (!extractorProcess) {
        logger.fatal('[System] Impossible de démarrer MediaExtractor.exe');
        process.exit(1);
    }

    const rl = readline.createInterface({
        input: extractorProcess.stdout as NodeJS.ReadableStream,
        crlfDelay: Infinity
    });

    rl.on('line', (line) => {
        try {
            if (!line || line.trim() === '') return;

            const rawJson = JSON.parse(line);
            const parsed = MediaPayloadSchema.safeParse(rawJson);

            if (!parsed.success) {
                logger.warn({ issues: parsed.error.issues }, '[Zod] Payload invalide rejeté');
                return;
            }

            updateDiscordActivity(parsed.data);
        } catch (e) {
            logger.error({ err: e }, '[Core] Erreur de parsing JSON provenant de l\'extracteur');
        }
    });

    if (extractorProcess && extractorProcess.stderr) {
        extractorProcess.stderr.on('data', (data) => {
            logger.error(`[Extractor STDERR] ${data.toString()}`);
        });
    }

    extractorProcess.on('close', (code) => {
        logger.warn(`[Extractor] Processus terminé (Code ${code}). Redémarrage dans 2s...`);
        setTimeout(startNativeExtractor, 2000);
    });
}

function initialize() {
    logger.info('[System] Initialisation Core v2.0 (Sécurisé)');

    rpcClient.on('ready', () => {
        logger.info(`[RPC] Connecté à Discord avec le client ${rpcClient.user?.username}`);
        isRpcReady = true;
        startNativeExtractor();
    });

    const connectToDiscord = () => {
        rpcClient.login({ clientId: CLIENT_ID }).catch((err: any) => {
            logger.error(`[RPC] Echec de connexion Discord: ${err.message}. Nouvelle tentative dans 10s...`);
            setTimeout(connectToDiscord, 10000);
        });
    };

    connectToDiscord();

    process.on('SIGINT', cleanup);
    process.on('SIGTERM', cleanup);
    process.on('uncaughtException', (err) => {
        logger.fatal({ err }, '[System] Exception Crash');
        cleanup();
        process.exit(1);
    });
}

function cleanup() {
    logger.info('[System] Nettoyage avant fermeture...');
    if (extractorProcess && !extractorProcess.killed) {
        extractorProcess.kill();
    }
}

initialize();
