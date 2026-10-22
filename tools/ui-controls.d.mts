import type { Plugin } from 'vite';
export function uiControls(): Plugin;
export function transformControls(code: string, filename: string): { code: string; map: unknown; count: number };
