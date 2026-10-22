export const settingsSections = [
  { id: 'account', group: 'personal', label: 'Hesap ve güvenlik', description: 'Parola, profil PIN’i ve açık oturumların.', access: 'all', keywords: 'account security password pin login hesap parola guvenlik oturum' },
  { id: 'interface', group: 'personal', label: 'Dil ve kontroller', description: 'Arayüz dili, gamepad ve TV kumandası.', access: 'all', keywords: 'language appearance english turkish gamepad controller remote dil gorunum kumanda' },
  { id: 'playback', group: 'personal', label: 'Oynatma', description: 'Oynatma sınırların, cihazların ve atlama tercihlerin.', access: 'all', keywords: 'playback streaming streams devices skip ses audio video kalite quality' },
  { id: 'devices', group: 'personal', label: 'Cihaz bağlama', description: 'Yeni bir TV, telefon veya tarayıcıyı hesabına bağla.', access: 'all', keywords: 'devices pairing link tv phone telefon kod baglama' },
  { id: 'downloads', group: 'personal', label: 'İndirme sınırları', description: 'Hesabının indirme bütçesi ve kullanım bilgileri.', access: 'acquire', keywords: 'downloads budget quota indirme limit kota butce' },
  { id: 'notifications', group: 'personal', label: 'Bildirimler', description: 'Tarayıcı bildirimi, e-posta ve webhook kanalları.', access: 'all', keywords: 'notifications push smtp email webhook bildirim eposta' },
  { id: 'libraries', group: 'server', label: 'Kütüphaneler', description: 'Medya klasörleri, tarama ve eşleştirme tercihleri.', access: 'admin', keywords: 'libraries folder scan matching kutuphane klasor tarama' },
  { id: 'sources', group: 'server', label: 'Kaynaklar ve kalite', description: 'Kaynak siteleri, kalite profilleri ve Usenet hesabı.', access: 'admin', keywords: 'sources indexer torrent usenet nntp quality profiles kaynak kalite' },
  { id: 'metadata', group: 'server', label: 'Film ve dizi bilgileri', description: 'TMDB bağlantısı ve içerik bilgisi dili.', access: 'admin', keywords: 'metadata tmdb catalog language film dizi afis poster icerik' },
  { id: 'subtitles', group: 'server', label: 'Altyazılar', description: 'OpenSubtitles bağlantısı, diller ve otomasyon.', access: 'admin', keywords: 'subtitles opensubtitles languages forced hi altyazi dublaj' },
  { id: 'household', group: 'server', label: 'Hane ve izinler', description: 'Hesaplar, davetler, ebeveyn sınırları ve izinler.', access: 'admin', keywords: 'household family users permissions parental invitations hane aile kullanici davet' },
  { id: 'operations', group: 'server', label: 'Yedek, ağ ve bakım', description: 'Yedek, ağ, HTTPS, güncelleme ve bakım.', access: 'admin', keywords: 'server backup restore network https proxy update encoding gpu nvenc h264 logs whisper asr sunucu yedek ag guncelleme' },
  { id: 'about', group: 'about', label: 'Home hakkında', description: 'Sürüm, sunucu bilgileri ve oynatma yetenekleri.', access: 'all', keywords: 'about version system capabilities surum hakkinda' },
] as const;
export type SettingsSection = typeof settingsSections[number]['id'];
export interface SettingsAccess { role: string | null; canAcquire: boolean }
export function visibleSettings(access: SettingsAccess) { return settingsSections.filter(section => section.access === 'all' || section.access === 'admin' && access.role === 'admin' || section.access === 'acquire' && (access.canAcquire || access.role === 'admin')); }
const fold = (value: string) => value.toLocaleLowerCase('tr').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/ı/g, 'i');
export function findSettings(access: SettingsAccess, query: string, translate: (value: string) => string = value => value) {
  const words = fold(query).trim().split(/\s+/).filter(Boolean);
  return visibleSettings(access).filter(section => { const text = fold(`${section.label} ${translate(section.label)} ${section.description} ${translate(section.description)} ${section.keywords}`); return words.every(word => text.includes(word)); });
}
