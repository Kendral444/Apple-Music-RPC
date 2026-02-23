import { z } from 'zod';

export const MediaPayloadSchema = z.object({
    source: z.string().optional().default('Unknown'),
    title: z.string(),
    artist: z.string(),
    album: z.string().optional().default(''),
    status: z.enum(['Playing', 'Paused', 'Stopped', 'Error']),
    startTime: z.number().optional(),
    endTime: z.number().optional()
});

export type MediaPayload = z.infer<typeof MediaPayloadSchema>;

export const ItunesTrackSchema = z.object({
    wrapperType: z.string(),
    term: z.string().optional(),
    artistName: z.string().optional(),
    trackName: z.string().optional(),
    artworkUrl100: z.string().optional(),
    trackViewUrl: z.string().optional()
});

export const ItunesResponseSchema = z.object({
    resultCount: z.number(),
    results: z.array(ItunesTrackSchema).optional()
});

export const DeezerTrackSchema = z.object({
    title: z.string().optional(),
    artist: z.object({ name: z.string().optional() }).optional(),
    album: z.object({ cover_xl: z.string().optional() }).optional(),
    link: z.string().optional()
});

export const DeezerResponseSchema = z.object({
    data: z.array(DeezerTrackSchema).optional()
});
