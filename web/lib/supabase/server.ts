import {createServerClient} from '@supabase/ssr';
import {cookies} from 'next/headers';
import {SUPABASE_ANON_KEY, SUPABASE_URL} from './config';

/**
 * Klien untuk Server Component, Route Handler, dan Server Action. Tetap memakai
 * session user, jadi setiap query lewat RLS seperti dari browser.
 */
export async function createClient() {
  const store = await cookies();

  return createServerClient(SUPABASE_URL, SUPABASE_ANON_KEY, {
    cookies: {
      getAll() {
        return store.getAll();
      },
      setAll(items) {
        try {
          for (const {name, value, options} of items) {
            store.set(name, value, options);
          }
        } catch {
          // Server Component tidak boleh menulis cookie. Penyegaran token sudah
          // ditangani middleware, jadi di sini kegagalannya tidak berakibat apa pun.
        }
      }
    }
  });
}

/**
 * User yang sedang masuk, atau null. Memakai getUser() dan bukan getSession()
 * karena getUser() memverifikasi token ke server — isi cookie bisa dipalsukan.
 */
export async function currentUser() {
  const supabase = await createClient();
  const {
    data: {user}
  } = await supabase.auth.getUser();
  return user;
}
