/**
 * URL project dan publishable (anon) key. Keduanya publik: isolasi data dijaga RLS,
 * bukan kerahasiaan key. `service_role` key tidak pernah masuk kode klien —
 * kalau terasa perlu bypass RLS, biasanya policy-nya yang salah.
 *
 * Nilai bawaan ada di sini supaya `pnpm build` dan deploy pertama ke Vercel jalan
 * tanpa konfigurasi. Environment variable tetap menang kalau diisi.
 */

const DEFAULT_SUPABASE_URL = 'https://fotpduvbevnncugruaom.supabase.co';
const DEFAULT_SUPABASE_ANON_KEY = 'sb_publishable_caz_LIyAsoPmX4md3cI6aA_8tLbB4vo';

export const SUPABASE_URL = process.env.NEXT_PUBLIC_SUPABASE_URL ?? DEFAULT_SUPABASE_URL;

export const SUPABASE_ANON_KEY = process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY ?? DEFAULT_SUPABASE_ANON_KEY;

/**
 * Dipakai halaman `/setup` untuk membedakan "environment variable terbaca" dari
 * "diam-diam jatuh ke nilai bawaan". Keduanya tampak sama saat aplikasi jalan,
 * dan itu yang membuat salah ketik di dashboard Vercel sulit dilacak.
 */
export const SUPABASE_URL_FROM_ENV = process.env.NEXT_PUBLIC_SUPABASE_URL !== undefined;
export const SUPABASE_ANON_KEY_FROM_ENV = process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY !== undefined;

/**
 * Asal situs untuk tautan login yang dikirim lewat email.
 *
 * Di browser, satu-satunya jawaban yang selalu benar adalah asal halaman yang
 * sedang dibuka — dan di sanalah fungsi ini dipakai, saat menyusun
 * `emailRedirectTo`. Env var seperti `VERCEL_URL` tidak ikut ke bundle browser,
 * jadi tanpa cabang ini tautan masuk yang dikirim dari production tetap menunjuk
 * localhost, dan tidak ada seorang pun yang bisa masuk.
 *
 * Di server, urutannya: setelan eksplisit, domain production Vercel (stabil
 * antar deploy), URL deployment (berubah tiap deploy), lalu localhost.
 */
export function siteUrl(): string {
  if (typeof window !== 'undefined') return window.location.origin;

  const explicit = process.env.NEXT_PUBLIC_SITE_URL;
  if (explicit && explicit.length > 0) return normalize(explicit);

  const production =
    process.env.NEXT_PUBLIC_VERCEL_PROJECT_PRODUCTION_URL ?? process.env.VERCEL_PROJECT_PRODUCTION_URL;
  if (production && production.length > 0) return normalize(production);

  const deployment = process.env.NEXT_PUBLIC_VERCEL_URL ?? process.env.VERCEL_URL;
  if (deployment && deployment.length > 0) return normalize(deployment);

  return 'http://localhost:3000';
}

/** Vercel memberi host tanpa skema; setelan manual biasanya sudah memakainya. */
function normalize(value: string): string {
  const trimmed = value.replace(/\/+$/, '');
  return /^https?:\/\//.test(trimmed) ? trimmed : `https://${trimmed}`;
}
