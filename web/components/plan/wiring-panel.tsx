'use client';

import {ChevronDown, ChevronRight, TriangleAlert} from 'lucide-react';
import {useTranslations} from 'next-intl';
import {useMemo} from 'react';
import {Badge, Notice, Select} from '@/components/ui';
import {switchStyleFor} from '@/lib/symbols';
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

  /**
   * Kaki saklar yang benar-benar ada di denah ini, bukan sebanyak yang dipilih di
   * dropdown: ruangan berisi satu titik tidak menghasilkan kaki, jadi saklar terakhir
   * bisa saja tidak terpakai. Menyebutnya di keterangan padahal tidak ada garisnya
   * membuat orang mencari warna yang tidak pernah muncul.
   */
  const legs = useMemo(() => {
    const devices = new Map<number, number>();

    for (const run of plan?.runs ?? []) {
      devices.set(run.switchIndex, (devices.get(run.switchIndex) ?? 0) + run.deviceIds.length);
    }

    return [...devices.entries()]
      .sort((a, b) => a[0] - b[0])
      .map(([switchIndex, count]) => ({switchIndex, devices: count}));
  }, [plan]);

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

          {/*
            Warna tiap saklar, beserta jumlah titik yang dipikulnya. Angka itu yang
            memberi tahu pembagiannya merata atau tidak — di ruangan berjumlah titik
            ganjil, satu kaki memang selalu kebagian satu lebih banyak.
          */}
          {legs.length > 0 ? (
            <div>
              <p className="mb-2 text-[11px] font-semibold tracking-wide text-muted uppercase">
                {t('legs')}
              </p>
              <ul className="space-y-2">
                {legs.map((leg) => {
                  const wire = switchStyleFor(leg.switchIndex);

                  return (
                    <li key={leg.switchIndex} className="flex items-center gap-2.5">
                      <svg viewBox="0 -6 40 12" className="h-3 w-10 shrink-0" aria-hidden>
                        <path
                          d="M 0 0 L 40 0"
                          fill="none"
                          stroke={wire.color}
                          strokeWidth={3}
                          strokeLinecap="round"
                          strokeDasharray={
                            wire.dash
                              ? wire.dash
                                  .split(' ')
                                  .map((part) => Number(part) * 3)
                                  .join(' ')
                              : undefined
                          }
                        />
                      </svg>
                      <span className="text-[13px] font-semibold">
                        {t('switchName', {number: leg.switchIndex + 1})}
                      </span>
                      <span className="ml-auto shrink-0 text-[12px] text-muted">
                        {t('roomDevices', {count: leg.devices})}
                      </span>
                    </li>
                  );
                })}
              </ul>
            </div>
          ) : null}

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
