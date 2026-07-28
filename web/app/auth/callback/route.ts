import {NextResponse, type NextRequest} from 'next/server';
import {createClient} from '@/lib/supabase/server';
import {routing} from '@/i18n/routing';

/**
 * Tempat mendarat tautan masuk dari email. Menukar kode jadi session, lalu
 * melanjutkan ke halaman tujuan.
 *
 * Route ini tidak diberi prefix locale — lihat matcher di middleware.ts.
 */
export async function GET(request: NextRequest) {
  const url = new URL(request.url);
  const code = url.searchParams.get('code');
  const next = safeNext(url.searchParams.get('next'));

  if (!code) {
    return NextResponse.redirect(new URL(`/${routing.defaultLocale}/login?error=callback`, url.origin));
  }

  const supabase = await createClient();
  const {error} = await supabase.auth.exchangeCodeForSession(code);

  if (error) {
    return NextResponse.redirect(new URL(`${loginFor(next)}?error=callback`, url.origin));
  }

  return NextResponse.redirect(new URL(next, url.origin));
}

/**
 * `next` datang dari query string, jadi tidak dipercaya: hanya path relatif di situs
 * ini yang diterima. Tanpa ini, tautan email bisa dipakai untuk mengarahkan orang
 * ke domain lain setelah login.
 */
function safeNext(candidate: string | null): string {
  const fallback = `/${routing.defaultLocale}/projects`;
  if (!candidate) return fallback;
  if (!candidate.startsWith('/') || candidate.startsWith('//')) return fallback;
  return candidate;
}

function loginFor(next: string): string {
  const locale = next.split('/')[1];
  return routing.locales.includes(locale as (typeof routing.locales)[number])
    ? `/${locale}/login`
    : `/${routing.defaultLocale}/login`;
}
