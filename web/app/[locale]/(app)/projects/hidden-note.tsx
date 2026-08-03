'use client';

import {useTranslations} from 'next-intl';
import {useRouter} from 'next/navigation';
import {useTransition} from 'react';
import {Button} from '@/components/ui';
import {showHiddenProjects} from './actions';

/**
 * Jalan pulang untuk project yang disembunyikan.
 *
 * Muncul hanya kalau ada yang tersembunyi. Tanpa ini, satu klik salah cuma bisa dibatalkan
 * dengan membuka Revit dan mengirim tarikan model — pintu satu arah untuk aksi yang justru
 * sengaja dibuat murah karena tidak menghapus apa pun.
 */
export function HiddenNote({count}: {count: number}) {
  const t = useTranslations('projects');
  const router = useRouter();
  const [pending, startTransition] = useTransition();

  if (count === 0) return null;

  return (
    <div className="flex items-center gap-3 px-1 text-[12px] text-muted">
      <span>{t('hiddenCount', {count})}</span>
      <Button
        type="button"
        tone="quiet"
        className="px-1 py-0 text-[12px]"
        disabled={pending}
        onClick={() => startTransition(async () => {
          await showHiddenProjects();
          router.refresh();
        })}
      >
        {t('showHidden')}
      </Button>
    </div>
  );
}
