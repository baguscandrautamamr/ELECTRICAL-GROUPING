'use client';

import {ChevronDown, ChevronRight, TriangleAlert} from 'lucide-react';
import {useTranslations} from 'next-intl';
import {Badge, Notice, Select} from '@/components/ui';
import {WIRE_STYLES, type WireStyle, type WiringOptions, type WiringPlan} from '@/lib/wiring';

/**
 * Kontrol wiring per ruangan.
 *
 * Presentasi saja: seluruh pilihan hidup di halaman denah, dan hitungannya di
 * `lib/wiring.ts`. Komponen ini tidak menyentuh state seleksi, circuit, maupun panel —
 * membuka atau menutupnya tidak bisa memengaruhi grouping yang sedang dikerjakan.
 */
export function WiringPanel({
  open,
  onToggle,
  options,
  onOptionsChange,
  plan
}: {
  open: boolean;
  onToggle: () => void;
  options: WiringOptions;
  onOptionsChange: (next: WiringOptions) => void;
  /** Null selagi section tertutup — tidak ada gunanya menghitung yang tidak dilihat. */
  plan: WiringPlan | null;
}) {
  const t = useTranslations('wiring');

  const inferred = plan?.rooms.filter((room) => room.inferred).length ?? 0;

  return (
    <section className="card p-5">
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={open}
        className="flex w-full items-center gap-2 text-left"
      >
        {open ? (
          <ChevronDown className="size-4 text-muted" aria-hidden />
        ) : (
          <ChevronRight className="size-4 text-muted" aria-hidden />
        )}
        <span className="text-[15px] font-semibold tracking-tight">{t('heading')}</span>
        {open && plan ? (
          <span className="ml-auto">
            <Badge tone="accent">{t('runCount', {count: plan.runs.length})}</Badge>
          </span>
        ) : null}
      </button>

      {open ? (
        <div className="mt-4 space-y-4">
          <p className="text-[12px] leading-relaxed text-muted">{t('intro')}</p>

          {/*
            Line style hidup di model Revit, dan belum ada tabel yang membawanya ke
            sini. Dropdown-nya tetap ditampilkan dalam keadaan mati: yang kosong dan
            disebutkan alasannya lebih jujur daripada kontrol yang belum ada sama
            sekali, karena pratinjau memang tidak butuh style untuk menggambar.
          */}
          <Select label={t('lineStyle')} value="" disabled onChange={() => {}}>
            <option value="">{t('lineStyleEmpty')}</option>
          </Select>
          <p className="text-[12px] leading-relaxed text-muted">{t('lineStyleHint')}</p>

          <Select
            label={t('style')}
            value={options.style}
            onChange={(event) => onOptionsChange({...options, style: event.target.value as WireStyle})}
          >
            {WIRE_STYLES.map((style) => (
              <option key={style} value={style}>
                {t(`style_${style}`)}
              </option>
            ))}
          </Select>

          <p className="text-[12px] leading-relaxed text-muted">{t(`styleHint_${options.style}`)}</p>

          {/*
            Menyilang butuh sepasang kolom, dan pasangan hanya punya dua sisi — jumlah
            saklarnya tidak bisa dipilih. Pemilihnya disembunyikan alih-alih ditampilkan
            mati: kontrol yang ada tapi tidak pernah berpengaruh lebih membingungkan
            daripada kontrol yang memang tidak ada di gaya itu.
          */}
          {options.style === 'crossing' ? null : (
            <Select
              label={t('switches')}
              value={String(options.switches)}
              onChange={(event) => onOptionsChange({...options, switches: Number(event.target.value)})}
            >
              {[1, 2, 3].map((count) => (
                <option key={count} value={count}>
                  {t('switchCount', {count})}
                </option>
              ))}
            </Select>
          )}

          {plan && inferred > 0 ? (
            <Notice tone="warn">{t('inferredWarning', {count: inferred})}</Notice>
          ) : null}

          {plan && plan.rooms.length > 0 ? (
            <div>
              <p className="mb-2 text-[11px] font-semibold tracking-wide text-muted uppercase">
                {t('rooms')}
              </p>
              <ul className="max-h-64 divide-y divide-hairline overflow-y-auto pr-1">
                {plan.rooms.map((room, index) => (
                  <li key={room.key} className="flex items-center gap-2 py-2 first:pt-0 last:pb-0">
                    <span className="min-w-0 flex-1 truncate text-[13px]">
                      {room.name ?? t('roomUnnamed', {number: index + 1})}
                    </span>
                    {room.inferred ? (
                      <TriangleAlert className="size-3.5 shrink-0 text-warn" aria-label={t('inferred')} />
                    ) : null}
                    <span className="shrink-0 text-[12px] text-muted">
                      {t('roomDevices', {count: room.devices})}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          ) : plan ? (
            <p className="text-[13px] text-muted">{t('noRooms')}</p>
          ) : null}

          <p className="text-[12px] leading-relaxed text-muted">{t('previewOnly')}</p>
        </div>
      ) : null}
    </section>
  );
}
