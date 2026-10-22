import { Children, Fragment, isValidElement, type ReactNode } from 'react';

export interface HomeSelectOption { value: string; label: string; disabled: boolean }
type OptionProps = { children?: ReactNode; value?: string | number; label?: string; disabled?: boolean };
function optionText(children: ReactNode): string {
  let text = '';
  Children.forEach(children, child => {
    if (typeof child === 'string' || typeof child === 'number') text += child;
    else if (isValidElement<OptionProps>(child)) text += optionText(child.props.children);
  });
  return text;
}
/** Declarative option children are data, never mounted as visible native controls. */
export function homeSelectOptions(children: ReactNode): HomeSelectOption[] {
  const options = new Map<string, HomeSelectOption>();
  const collect = (nodes: ReactNode, groupDisabled = false) => Children.forEach(nodes, child => {
    if (!isValidElement<OptionProps>(child)) return;
    if (child.type === Fragment) collect(child.props.children, groupDisabled);
    else if (child.type === 'optgroup') collect(child.props.children, groupDisabled || !!child.props.disabled);
    else if (child.type === 'option') {
      const text = optionText(child.props.children).replace(/\s+/g, ' ').trim();
      const value = child.props.value === undefined ? text : String(child.props.value);
      // As with a native select, the first matching value owns its identity.
      if (!options.has(value)) options.set(value, { value, label: child.props.label ?? text, disabled: groupDisabled || !!child.props.disabled });
    }
  });
  collect(children); return [...options.values()];
}
export function homeSelectKey(value: string) { return `home-option:${value}`; }
export function homeSelectValue(options: HomeSelectOption[], value: string | number | undefined) {
  const requested = value === undefined ? undefined : String(value);
  return options.find(option => option.value === requested) ?? options[0];
}
export function homeSelectChange(options: HomeSelectOption[], key: string | number | null, disabled = false) {
  if (disabled || key === null) return;
  const option = options.find(option => homeSelectKey(option.value) === key);
  if (!option || option.disabled) return;
  // Existing Home handlers consume only target.value. This is a typed value payload,
  // not a fabricated DOM event or an HTMLSelectElement cast.
  return { target: { value: option.value } };
}
