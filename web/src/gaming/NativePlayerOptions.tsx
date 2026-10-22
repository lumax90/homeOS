import type {Game} from './model';
import {GameSettingsButton} from './GameSettings';
export function NativePlayerOptions({game}:{game:Game}){return <GameSettingsButton game={game} inPlayer/>;}
