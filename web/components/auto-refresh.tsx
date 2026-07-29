'use client';

import {useRouter} from 'next/navigation';
import {useEffect} from 'react';

/**
 * Menarik ulang data server component secara berkala.
 *
 * Add-in mengirim model ke database sendiri saat ada perubahan di Revit, tapi halaman
 * yang sudah terbuka tidak tahu apa-apa soal itu. Tanpa ini, lampu atau family yang
 * baru ditambahkan baru muncul setelah user menekan muat ulang — dan yang terlihat
 * adalah web yang "tidak cocok dengan Revit", bukan web yang belum melihat lagi.
 *
 * Hanya menyegarkan saat tab benar-benar terlihat. Tab yang ditinggalkan berjam-jam
 * tidak perlu menghasilkan ratusan request, dan begitu dibuka lagi penyegaran
 * berikutnya sudah membawa keadaan terbaru.
 */
export function AutoRefresh({seconds}: {seconds: number}) {
  const router = useRouter();

  useEffect(() => {
    const timer = setInterval(() => {
      if (document.visibilityState === 'visible') router.refresh();
    }, seconds * 1000);

    return () => clearInterval(timer);
  }, [seconds, router]);

  return null;
}
