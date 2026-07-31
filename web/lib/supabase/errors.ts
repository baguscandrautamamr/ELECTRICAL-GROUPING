/**
 * Kegagalan query yang perlu dibedakan di layar.
 *
 * Sebelum ini semua kegagalan berakhir sebagai `data ?? []`, jadi database yang
 * belum dimigrasi tampil persis seperti akun yang memang belum punya data —
 * layar kosong yang mengajak bertindak, padahal tidak ada aksi yang akan jalan.
 */
export type LoadProblem = 'schema' | 'unknown';

type QueryError = {code?: string; message?: string} | null | undefined;

/**
 * PostgREST memakai `42P01` (relasi tidak ada) dari Postgres dan `PGRST20x` saat
 * tabel tidak ketemu di schema cache. Keduanya berarti hal yang sama bagi user:
 * migrasi belum pernah ditembakkan ke project Supabase ini.
 */
export function classifyError(error: QueryError): LoadProblem | null {
  if (!error) return null;

  const code = error.code ?? '';
  if (code === '42P01' || code === 'PGRST205' || code === 'PGRST202') return 'schema';

  const message = (error.message ?? '').toLowerCase();
  if (message.includes('does not exist') || message.includes('schema cache')) return 'schema';

  return 'unknown';
}

/** Masalah pertama dari beberapa query yang dijalankan berbarengan. */
export function firstProblem(...errors: QueryError[]): LoadProblem | null {
  for (const error of errors) {
    const problem = classifyError(error);
    if (problem) return problem;
  }
  return null;
}

/**
 * Data dari query yang boleh saja belum ada.
 *
 * Fitur yang datang lewat migrasi lebih baru — tabel `layout_devices` dan fungsi
 * penghitungnya — tidak boleh menjatuhkan seluruh halaman hanya karena migrasinya
 * belum ditembakkan ke project Supabase. Yang benar adalah kembali ke perilaku
 * sebelum fitur itu ada, bukan memasang layar "database belum disiapkan" di atas
 * database yang sebenarnya sehat.
 *
 * Jangan pakai ini untuk tabel inti. Kalau `projects` atau `devices` hilang, itu
 * memang penyiapan yang belum jalan, dan user berhak diberi tahu.
 */
export function optional<T>(result: {data: T | null; error: QueryError}): T | null {
  return result.error ? null : result.data;
}
