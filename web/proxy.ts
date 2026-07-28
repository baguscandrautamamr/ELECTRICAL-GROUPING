import {createServerClient} from '@supabase/ssr';
import createIntlMiddleware from 'next-intl/middleware';
import {NextResponse, type NextRequest} from 'next/server';
import {routing} from './i18n/routing';
import {SUPABASE_ANON_KEY, SUPABASE_URL} from './lib/supabase/config';

const handleIntl = createIntlMiddleware(routing);

/**
 * Dua tugas dalam satu jalur: memilih locale, dan menyegarkan session Supabase.
 * Urutannya penting — response dari next-intl yang dipakai menampung cookie baru,
 * supaya token yang disegarkan tidak hilang saat redirect locale terjadi.
 */
export default async function proxy(request: NextRequest) {
  const response = handleIntl(request);

  const supabase = createServerClient(SUPABASE_URL, SUPABASE_ANON_KEY, {
    cookies: {
      getAll() {
        return request.cookies.getAll();
      },
      setAll(items) {
        for (const {name, value, options} of items) {
          response.cookies.set(name, value, options);
        }
      }
    }
  });

  const {
    data: {user}
  } = await supabase.auth.getUser();

  const segments = request.nextUrl.pathname.split('/');
  const locale = routing.locales.includes(segments[1] as (typeof routing.locales)[number])
    ? segments[1]!
    : routing.defaultLocale;

  const onLogin = segments[2] === 'login';

  if (!user && !onLogin) {
    return redirectKeepingCookies(request, response, `/${locale}/login`);
  }

  if (user && onLogin) {
    return redirectKeepingCookies(request, response, `/${locale}/projects`);
  }

  return response;
}

/**
 * Redirect biasa akan membuang cookie yang baru saja diset klien Supabase, dan
 * akibatnya user seolah-olah keluar sendiri beberapa menit sekali.
 */
function redirectKeepingCookies(request: NextRequest, carrier: NextResponse, path: string) {
  const url = request.nextUrl.clone();
  url.pathname = path;
  url.search = '';

  const redirect = NextResponse.redirect(url);
  for (const cookie of carrier.cookies.getAll()) {
    redirect.cookies.set(cookie);
  }

  return redirect;
}

export const config = {
  // Lewati route handler auth, aset Next, dan file statis. `auth/callback`
  // bukan halaman, jadi tidak boleh diberi prefix locale.
  matcher: ['/((?!api|auth|_next|_vercel|.*\\..*).*)']
};
