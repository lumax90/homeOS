import { useSyncExternalStore } from 'react';
import { Button } from '@heroui/react';
import { Gamepad2 } from 'lucide-react';
import { translate as t, useUiLanguage } from '../i18n';
import { controllerActions, type ControllerAction } from '../controller/model';
import { useController } from '../controller/react';

const labels: Record<ControllerAction, string> = { up: 'Yukarı', down: 'Aşağı', left: 'Sol', right: 'Sağ', confirm: 'Seç', back: 'Geri', menu: 'Menü', search: 'Ara', previous: 'Önceki bölüm', next: 'Sonraki bölüm', playpause: 'Oynat / duraklat', seekback: 'Geri sar', seekforward: 'İleri sar', scrollup: 'Yukarı kaydır', scrolldown: 'Aşağı kaydır', scrollleft: 'Sola kaydır', scrollright: 'Sağa kaydır', focusplayer:'Oynatıcıya geç / geri dön' };
function Controls({ controller }: { controller: NonNullable<ReturnType<typeof useController>> }) {
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot); useUiLanguage();
  return <details className="controller-settings"><summary><Gamepad2 size={17}/>{t('Gamepad ve kumanda')}</summary><div className="home-form">
    <label className="check-field"><input type="checkbox" checked={state.preferences.enabled} onChange={event => controller.setPreferences({ enabled: event.target.checked })}/>{t('Gamepad ve yön tuşlarıyla gezinmeyi etkinleştir')}</label>
    <p className="field-hint">{t('Fare görünümü değişmez. Bir gezinme tuşuna bastığında odak ve tuş ipuçları açılır.')}</p>
    <p className="field-hint" role="status">{state.capability === 'insecure' ? t('Gamepad için bu sayfayı güvenilir HTTPS üzerinden aç. Kumanda yön tuşları HTTP üzerinde de çalışabilir.') : state.capability === 'unsupported' ? t('Bu tarayıcı Gamepad API desteklemiyor. Kumanda yön tuşları kullanılabilir.') : state.capability === 'blocked' ? t('Tarayıcı gamepad erişimini engelledi. Güvenli bağlantıyı ve tarayıcı politikasını kontrol et.') : !state.devices.length ? t('Gamepad algılanmadı. Cihazına bağla, sonra bir tuşa basıp bırak.') : t('Bağlı gamepad algılandı.')}</p>
    <label className="field"><span>{t('Analog boşluk eşiği')}</span><input type="range" min={.2} max={.8} step={.05} value={state.preferences.deadzone} onChange={event => controller.setPreferences({ deadzone: Number(event.target.value) })}/><small>{Math.round(state.preferences.deadzone * 100)}%</small></label>
    {state.devices.map(device => <section className="controller-device" key={device.index}><h4>{t('Gamepad')} {device.index + 1}</h4><p className="field-hint">{device.ready ? t('Standart veya atanmış tuş düzeni hazır.') : t('Standart olmayan gamepad: önce yön, seç ve geri tuşlarını ata.')}</p><details><summary>{t('Tuş atamalarını düzenle')}</summary><div className="controller-bindings">{controllerActions.map(action => <Button size="sm" variant="ghost" key={action} isDisabled={!!state.binding} onPress={() => controller.startBinding(device.id, action)}>{t(labels[action])}<small>{state.preferences.mappings[device.id]?.[action] === undefined ? '—' : state.preferences.mappings[device.id]?.[action]}</small></Button>)}</div><Button variant="ghost" onPress={() => controller.resetMapping(device.id)}>{t('Varsayılan tuşlara dön')}</Button></details></section>)}
    {state.binding && <div className="controller-binding-status" role="status"><span>{t('Atamak istediğin gamepad tuşuna basıp bırak.')} {t(labels[state.binding])}</span><Button variant="ghost" onPress={controller.cancelBinding}>{t('Vazgeç')}</Button></div>}
    <p className="field-hint">{t('Bu tarayıcıda saklanır. Her TV ve gamepad desteği gerçek cihazda doğrulanmalıdır.')}</p>
  </div></details>;
}
export function ControllerSettings() { const controller = useController(); return controller ? <Controls controller={controller}/> : null; }
