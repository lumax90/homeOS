import type { Plugin } from 'vite';
export function uiLocalization(): Plugin;
export function normalizeText(value: string): string;
export function localizationResources(): { english: Record<string, string>; turkish: Record<string, string> };
export function transformUi(code: string, filename: string, english?: Record<string, string>): { code: string; map: unknown };
