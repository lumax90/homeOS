import { useId, type ComponentProps } from 'react';

/** A shared glass material for controls. CSS blur is the baseline; the subtle
 * SVG displacement is enabled only when the browser supports backdrop URLs. */
export function Glass({ className = '', children, style, ...props }: ComponentProps<'div'>) {
  const id = `glass-${useId().replaceAll(':', '')}`;
  const refracts = typeof CSS !== 'undefined' && CSS.supports('backdrop-filter', 'url("#glass")')
    && !/Firefox|Version\/.*Safari/.test(navigator.userAgent);
  return (
    <div className={`glass ${className}`} style={{ ...style, ...(refracts ? { backdropFilter: `blur(var(--glass-blur, 18px)) saturate(155%) url(#${id})` } : {}) }} {...props}>
      <svg className="glass-filter" aria-hidden="true" width="0" height="0">
        <defs>
          <filter id={id} x="-10%" y="-10%" width="120%" height="120%">
            <feTurbulence type="fractalNoise" baseFrequency="0.012 0.03" numOctaves="1" seed="4" result="texture" />
            <feGaussianBlur in="texture" stdDeviation="3" result="soft" />
            <feDisplacementMap in="SourceGraphic" in2="soft" scale="2" xChannelSelector="R" yChannelSelector="G" />
          </filter>
        </defs>
      </svg>
      {children}
    </div>
  );
}
