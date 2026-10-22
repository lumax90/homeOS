import { useEffect } from 'react';
import { useUiLanguage } from './i18n';

export function usePageTitle(title: string | undefined, active = true) {
  const language = useUiLanguage();
  useEffect(() => {
    if (!title || !active) return;
    const previous = document.title; const owned = `${title} — Home`;
    document.title = owned;
    return () => { if (document.title === owned) document.title = previous; };
  }, [title, active, language]);
}
