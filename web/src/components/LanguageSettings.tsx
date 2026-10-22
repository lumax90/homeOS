import {setUiLanguage,useUiLanguage,translate as t,type UiLanguage} from '../i18n';

export function LanguageSettings(){
  const language=useUiLanguage();
  return <label className="field language-setting"><span>{t('Arayüz dili')}</span><select aria-label={t('Arayüz dili')} value={language} onChange={event=>{void setUiLanguage(event.target.value as UiLanguage);}}><option value="en">English</option><option value="tr">Türkçe</option></select><small className="field-hint">{t('Bu tarayıcıda saklanır. Film/dizi bilgisi ve altyazı dili ayrı ayarlanır.')}</small></label>;
}
