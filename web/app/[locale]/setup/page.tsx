import {CircleAlert, CircleCheck, CircleHelp} from 'lucide-react';
import {getTranslations, setRequestLocale} from 'next-intl/server';
import type {Metadata} from 'next';
import {headers} from 'next/headers';
import {Card, CardHeader, cx} from '@/components/ui';
import {LocaleToggle} from '@/components/locale-toggle';
import {ThemeToggle} from '@/components/theme-toggle';
import {Link} from '@/i18n/navigation';
import {classifyError} from '@/lib/supabase/errors';
import {
  SUPABASE_ANON_KEY,
  SUPABASE_ANON_KEY_FROM_ENV,
  SUPABASE_URL,
  SUPABASE_URL_FROM_ENV
} from '@/lib/supabase/config';
import {createClient} from '@/lib/supabase/server';

// Halaman ini melaporkan keadaan saat dibuka, jadi tidak boleh ikut ter-cache.
export const dynamic = 'force-dynamic';

export async function generateMetadata({
  params
}: {
  params: Promise<{locale: string}>;
}): Promise<Metadata> {
  const {locale} = await params;
  const t = await getTranslations({locale, namespace: 'setup'});
  return {title: t('heading'), robots: {index: false, follow: false}};
}

type State = 'ok' | 'fail' | 'unknown';

/**
 * Apakah Auth Supabase menjawab dengan key yang dipakai aplikasi. Ini yang
 * membedakan "key salah" dari "database belum dimigrasi" — dua sebab dengan
 * gejala yang sama di layar login.
 */
async function checkAuth(): Promise<{state: State; detail: string}> {
  try {
    const response = await fetch(`${SUPABASE_URL}/auth/v1/health`, {
      headers: {apikey: SUPABASE_ANON_KEY},
      cache: 'no-store',
      signal: AbortSignal.timeout(8000)
    });
    return {state: response.ok ? 'ok' : 'fail', detail: `HTTP ${response.status}`};
  } catch (error) {
    return {state: 'fail', detail: error instanceof Error ? error.name : 'fetch failed'};
  }
}

/**
 * Apakah migrasi sudah ditembakkan: satu query paling murah ke tabel `projects`.
 * `missing` dan `other` dipisahkan supaya saran yang ditampilkan tidak menyuruh
 * menjalankan migrasi padahal yang putus adalah koneksinya.
 */
async function checkSchema(): Promise<{state: State; detail: string; cause: 'missing' | 'other'}> {
  try {
    const supabase = await createClient();
    const {error} = await supabase.from('projects').select('id').limit(1);

    if (!error) return {state: 'ok', detail: 'projects', cause: 'other'};

    const missing = classifyError(error) === 'schema';
    return {
      state: 'fail',
      detail: missing ? (error.code ?? '42P01') : error.message,
      cause: missing ? 'missing' : 'other'
    };
  } catch (error) {
    return {
      state: 'fail',
      detail: error instanceof Error ? error.message : 'query failed',
      cause: 'other'
    };
  }
}

export default async function SetupPage({params}: {params: Promise<{locale: string}>}) {
  const {locale} = await params;
  setRequestLocale(locale);

  const t = await getTranslations('setup');
  const app = await getTranslations('app');

  const head = await headers();
  const host = head.get('x-forwarded-host') ?? head.get('host') ?? 'localhost:3000';
  const scheme = head.get('x-forwarded-proto') ?? (host.startsWith('localhost') ? 'http' : 'https');
  const origin = `${scheme}://${host}`;

  const [auth, schema] = await Promise.all([checkAuth(), checkSchema()]);

  const rows: Array<{state: State; label: string; value: string; hint?: string}> = [
    {
      state: SUPABASE_URL_FROM_ENV ? 'ok' : 'unknown',
      label: t('envUrl'),
      value: SUPABASE_URL,
      hint: SUPABASE_URL_FROM_ENV ? undefined : t('usingDefault')
    },
    {
      state: SUPABASE_ANON_KEY_FROM_ENV ? 'ok' : 'unknown',
      label: t('envKey'),
      value: mask(SUPABASE_ANON_KEY),
      hint: SUPABASE_ANON_KEY_FROM_ENV ? undefined : t('usingDefault')
    },
    {state: auth.state, label: t('authReachable'), value: auth.detail, hint: auth.state === 'ok' ? undefined : t('authHint')},
    {
      state: schema.state,
      label: t('schemaReady'),
      value: schema.detail,
      hint:
        schema.state === 'ok'
          ? undefined
          : schema.cause === 'missing'
            ? t('schemaHint')
            : t('schemaBlockedHint')
    },
    {state: 'unknown', label: t('redirectUrl'), value: `${origin}/auth/callback`, hint: t('redirectHint')}
  ];

  return (
    <main className="mx-auto w-full max-w-2xl px-5 py-12">
      <div className="mb-6 flex items-start justify-between gap-4">
        <div>
          <h1 className="text-[24px] font-semibold tracking-tight">{t('heading')}</h1>
          <p className="mt-1 text-[13px] leading-relaxed text-muted">{t('subheading')}</p>
        </div>
        <div className="flex shrink-0 items-center gap-1">
          <LocaleToggle />
          <ThemeToggle />
        </div>
      </div>

      <Card>
        <CardHeader title={app('name')} hint={origin} />
        <ul className="divide-y divide-hairline">
          {rows.map((row) => (
            <li key={row.label} className="flex items-start gap-3 py-3 first:pt-0 last:pb-0">
              <Mark state={row.state} />
              <div className="min-w-0 flex-1">
                <p className="text-[14px] font-semibold">{row.label}</p>
                <p className="mt-0.5 font-mono text-[12px] break-all text-muted">{row.value}</p>
                {row.hint ? (
                  <p className="mt-1 text-[12px] leading-relaxed text-muted">{row.hint}</p>
                ) : null}
              </div>
            </li>
          ))}
        </ul>
      </Card>

      <div className="mt-6 flex justify-center">
        <Link
          href="/login"
          className="rounded-[var(--radius-control)] border border-hairline bg-sunken px-3.5 py-2 text-[13px] font-semibold transition-colors duration-200 hover:border-muted"
        >
          {t('backToLogin')}
        </Link>
      </div>
    </main>
  );
}

function Mark({state}: {state: State}) {
  const Icon = state === 'ok' ? CircleCheck : state === 'fail' ? CircleAlert : CircleHelp;
  return (
    <Icon
      aria-hidden
      className={cx(
        'mt-0.5 size-4 shrink-0',
        state === 'ok' ? 'text-ok' : state === 'fail' ? 'text-danger' : 'text-muted'
      )}
    />
  );
}

/** Key ini publik, tapi menampilkannya utuh bikin orang mengira ia rahasia yang bocor. */
function mask(key: string): string {
  return key.length <= 12 ? key : `${key.slice(0, 8)}…${key.slice(-4)}`;
}
