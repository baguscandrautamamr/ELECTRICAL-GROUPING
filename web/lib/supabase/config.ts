/**
 * URL project dan publishable (anon) key. Keduanya publik: isolasi data dijaga RLS,
 * bukan kerahasiaan key. `service_role` key tidak pernah masuk kode klien —
 * kalau terasa perlu bypass RLS, biasanya policy-nya yang salah.
 *
 * Nilai bawaan ada di sini supaya `pnpm build` dan deploy pertama ke Vercel jalan
 * tanpa konfigurasi. Environment variable tetap menang kalau diisi.
 */

export const SUPABASE_URL =
  process.env.NEXT_PUBLIC_SUPABASE_URL ?? 'https://fotpduvbevnncugruaom.supabase.co';

export const SUPABASE_ANON_KEY =
  process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY ?? 'sb_publishable_caz_LIyAsoPmX4md3cI6aA_8tLbB4vo';

/**
 * Asal situs untuk tautan login yang dikirim lewat email. Tautan yang menunjuk
 * localhost tidak berguna di production, jadi urutannya: setelan eksplisit,
 * lalu URL yang diberikan Vercel, lalu localhost.
 */
export function siteUrl(): string {
  const explicit = process.env.NEXT_PUBLIC_SITE_URL;
  if (explicit && explicit.length > 0) return explicit.replace(/\/$/, '');

  const vercel = process.env.NEXT_PUBLIC_VERCEL_URL ?? process.env.VERCEL_URL;
  if (vercel && vercel.length > 0) return `https://${vercel.replace(/\/$/, '')}`;

  return 'http://localhost:3000';
}
