'use client';

import {useTranslations} from 'next-intl';
import {DEVICE_STATUSES, splitFamilyKey, type DeviceStatus} from '@/lib/contract';
import {STATUS_STYLE, geometryFor, symbolFor} from '@/lib/symbols';

/** Contoh satu simbol, dipakai di keterangan supaya bentuk dan garisnya bisa dibandingkan. */
function Swatch({status, familyKey}: {status: DeviceStatus; familyKey: string}) {
  const style = STATUS_STYLE[status];
  const geometry = geometryFor(symbolFor(familyKey), 7);
  const fill =
    style.fill === 'solid'
      ? style.color
      : style.fill === 'muted'
        ? 'color-mix(in oklab, var(--ink-muted) 20%, transparent)'
        : 'var(--surface)';

  return (
    <svg viewBox="-12 -12 24 24" className="size-5 shrink-0" aria-hidden>
      {geometry.kind === 'circle' ? (
        <circle r={geometry.r} fill={fill} stroke={style.color} strokeWidth={2} strokeDasharray={style.dash} />
      ) : (
        <polygon
          points={geometry.points}
          fill={fill}
          stroke={style.color}
          strokeWidth={2}
          strokeDasharray={style.dash}
        />
      )}
      {style.pip ? <circle r={2.4} fill={style.color} /> : null}
      {style.struck ? <line x1={-8} y1={8} x2={8} y2={-8} stroke={style.color} strokeWidth={2} /> : null}
    </svg>
  );
}

export function Legend({familyKeys}: {familyKeys: string[]}) {
  const t = useTranslations('status');
  const plan = useTranslations('plan');

  return (
    <div className="space-y-4">
      <div>
        <p className="mb-2 text-[11px] font-semibold tracking-wide text-muted uppercase">{plan('legend')}</p>
        <ul className="space-y-2">
          {DEVICE_STATUSES.map((status) => (
            <li key={status} className="flex items-start gap-2.5">
              <Swatch status={status} familyKey="legend" />
              <div className="min-w-0">
                <p className="text-[13px] font-semibold">{t(status)}</p>
                <p className="text-[12px] leading-snug text-muted">{t(`${status}Hint`)}</p>
              </div>
            </li>
          ))}
        </ul>
      </div>

      {familyKeys.length > 0 ? (
        <div>
          <p className="mb-2 text-[11px] font-semibold tracking-wide text-muted uppercase">
            {plan('families')}
          </p>
          <ul className="space-y-1.5">
            {familyKeys.map((key) => {
              const {family, type} = splitFamilyKey(key);
              return (
                <li key={key} className="flex items-center gap-2.5">
                  <Swatch status="unwired" familyKey={key} />
                  <span className="min-w-0 truncate text-[12px]">
                    <span className="font-semibold">{family}</span>
                    {type ? <span className="text-muted"> · {type}</span> : null}
                  </span>
                </li>
              );
            })}
          </ul>
        </div>
      ) : null}
    </div>
  );
}
