/**
 * Mengambil seluruh baris sebuah query, berapa pun jumlahnya.
 *
 * PostgREST memotong setiap select di seribu baris — tanpa error, tanpa tanda di
 * jawabannya. Yang terlihat hanyalah halaman yang isinya kurang, dan itu jenis
 * kesalahan yang paling lama tidak ketahuan: sepuluh denah pertama benar, denah gudang
 * ke sebelas kehilangan sepertiga lampunya.
 *
 * Batas itu berlaku untuk setiap tabel yang tumbuh seiring besar model — device,
 * keanggotaan layout, saklar, circuit. Tabel yang jumlahnya dibatasi bentuk project
 * (panel, override simbol per family) tidak perlu lewat sini.
 */

/**
 * Bentuk hasil satu halaman. Ditulis struktural, bukan meminjam tipe builder Supabase:
 * yang dibutuhkan di sini hanya `data` dan `error`.
 */
export type QueryPage = PromiseLike<{
  data: unknown[] | null;
  error: {code?: string; message?: string} | null;
}>;

/** Sekali ambil bisa memuat paling banyak seribu baris di PostgREST. */
export const PAGE = 1000;

/**
 * Hasilnya dibentuk seperti hasil query Supabase — `{data, error}` — supaya bisa
 * langsung masuk ke `firstProblem()` dan `optional()` bersama query lain.
 *
 * Urutan wajib ditentukan pemanggilnya lewat `order`. Tanpa itu halaman kedua tidak
 * dijamin melanjutkan halaman pertama, dan yang hilang bukan "seribu baris terakhir"
 * melainkan baris sembarang — gejala yang jauh lebih sulit dikenali daripada potongan
 * di ujung.
 */
export async function allRows<T>(
  page: (from: number, to: number) => QueryPage
): Promise<{data: T[] | null; error: {code?: string; message?: string} | null}> {
  const rows: T[] = [];

  for (let from = 0; ; from += PAGE) {
    const {data, error} = await page(from, from + PAGE - 1);

    if (error) return {data: null, error};

    rows.push(...((data ?? []) as T[]));
    if ((data?.length ?? 0) < PAGE) return {data: rows, error: null};
  }
}
