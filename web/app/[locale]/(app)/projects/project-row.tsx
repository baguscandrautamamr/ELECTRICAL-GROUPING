'use client';

import {ChevronRight, Trash2} from 'lucide-react';
import {useTranslations} from 'next-intl';
import {useRouter} from 'next/navigation';
import {useState, useTransition} from 'react';
import {Badge, Button, Notice} from '@/components/ui';
import {Link} from '@/i18n/navigation';
import {deleteProject} from './actions';

/**
 * Satu baris di daftar project.
 *
 * Client component karena hapus butuh langkah konfirmasi dan tempat menaruh pesan
 * gagal — keduanya state, dan keduanya milik satu baris saja. Waktu "diperbarui"
 * tetap diformat di server dan masuk sebagai teks jadi, supaya format tanggal tidak
 * ikut berpindah ke browser.
 *
 * Barisnya tidak lagi satu <Link> utuh. Tombol di dalam anchor bukan HTML yang sah,
 * dan menyarangkannya membuat satu klik menghapus sekaligus berpindah halaman. Yang
 * menggantikan area klik selebar baris adalah overlay `::after` milik Link; tombol
 * hapus diberi posisi sendiri supaya duduk di atas overlay itu, bukan di bawahnya.
 */
export function ProjectRow({id, name, updatedLabel}: {id: string; name: string; updatedLabel: string}) {
  const t = useTranslations('projects');
  const errors = useTranslations('errors');
  const router = useRouter();

  const [confirming, setConfirming] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  function remove() {
    setFailure(null);

    startTransition(async () => {
      const result = await deleteProject(id);
      setConfirming(false);

      if (result.ok) {
        // Barisnya hilang dari daftar — itu jawaban yang lebih jelas daripada pesan
        // yang muncul di tempat yang baru saja dikosongkan.
        router.refresh();
        return;
      }

      setFailure(
        result.reason === 'forbidden'
          ? t('deleteOwnerOnly')
          : result.reason === 'gone'
            ? t('deleteGone')
            : result.reason === 'schema'
              ? errors('schemaMissingBody')
              : errors('unknown')
      );
    });
  }

  return (
    <li>
      <div className="relative flex items-center gap-3 px-5 py-4 transition-colors duration-200 hover:bg-sunken">
        <div className="min-w-0 flex-1">
          <Link href={`/projects/${id}`} className="after:absolute after:inset-0 after:content-['']">
            <p className="truncate text-[15px] font-semibold">{name}</p>
          </Link>
          <p className="mt-0.5 text-[12px] text-muted">{updatedLabel}</p>
        </div>

        {confirming ? (
          <div className="relative z-10 flex items-center gap-1">
            <Button type="button" tone="danger" onClick={remove} disabled={pending}>
              {t('deleteConfirm')}
            </Button>
            <Button type="button" tone="quiet" onClick={() => setConfirming(false)} disabled={pending}>
              {t('deleteCancel')}
            </Button>
          </div>
        ) : (
          <Button
            type="button"
            tone="danger"
            className="relative z-10 px-2"
            aria-label={t('deleteLabel', {name})}
            onClick={() => {
              setFailure(null);
              setConfirming(true);
            }}
          >
            <Trash2 className="size-4" aria-hidden />
          </Button>
        )}

        <Badge tone="accent">{t('open')}</Badge>
        <ChevronRight className="size-4 shrink-0 text-muted" aria-hidden />
      </div>

      {confirming ? (
        <p className="px-5 pb-4 text-[12px] leading-relaxed text-muted">{t('deleteWarning')}</p>
      ) : null}

      {failure ? (
        <div className="px-5 pb-4">
          <Notice tone="danger">{failure}</Notice>
        </div>
      ) : null}
    </li>
  );
}
