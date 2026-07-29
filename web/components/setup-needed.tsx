import {getTranslations} from 'next-intl/server';
import {Link} from '@/i18n/navigation';
import type {LoadProblem} from '@/lib/supabase/errors';

/**
 * Layar untuk kegagalan yang tidak bisa diperbaiki user dari dalam aplikasi:
 * database belum dimigrasi, atau Supabase menolak query. Menyebut penyebabnya dan
 * mengarahkan ke `/setup`, bukan menampilkan layar kosong yang menyesatkan.
 */
export async function SetupNeeded({problem}: {problem: LoadProblem}) {
  const t = await getTranslations('errors');

  return (
    <div className="rounded-[var(--radius-card)] border border-dashed border-danger/40 px-6 py-10 text-center">
      <p className="text-[14px] font-semibold text-danger">
        {problem === 'schema' ? t('schemaMissing') : t('loadFailed')}
      </p>
      <p className="mx-auto mt-2 max-w-md text-[13px] leading-relaxed text-muted">
        {problem === 'schema' ? t('schemaMissingBody') : t('loadFailedBody')}
      </p>
      <div className="mt-5 flex justify-center">
        <Link
          href="/setup"
          className="rounded-[var(--radius-control)] border border-hairline bg-sunken px-3.5 py-2 text-[13px] font-semibold transition-colors duration-200 hover:border-muted"
        >
          {t('openSetup')}
        </Link>
      </div>
    </div>
  );
}
