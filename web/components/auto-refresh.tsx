'use client';

import {useRouter} from 'next/navigation';
import {useEffect} from 'react';

/**
 * Menarik ulang data server component saat keadaan mungkin sudah berubah.
 *
 * Add-in mengirim model ke database sendiri saat ada perubahan di Revit, tapi halaman
 * yang sudah terbuka tidak tahu apa-apa soal itu. Tanpa ini, lampu atau family yang
 * baru ditambahkan baru muncul setelah user menekan muat ulang — dan yang terlihat
 * adalah web yang "tidak cocok dengan Revit", bukan web yang belum melihat lagi.
 *
 * Yang menanggung sebagian besar pekerjaannya adalah kembalinya fokus, bukan timer.
 * Alur kerja sebenarnya adalah menggambar di Revit lalu berpindah ke browser, jadi
 * menyegarkan tepat saat tab kembali terlihat terasa seketika — dan tidak berongkos
 * apa pun selama tab ditinggalkan. Timer hanya jaring pengaman untuk layar yang
 * dibiarkan terbuka di sebelah Revit.
 */
export function AutoRefresh({seconds}: {seconds: number}) {
  const router = useRouter();

  useEffect(() => {
    let lastSeenHidden = document.visibilityState === 'hidden';

    function onVisibility() {
      // Hanya saat berpindah dari tersembunyi ke terlihat. Browser membangkitkan
      // event ini juga saat tab disembunyikan, dan menyegarkan di situ percuma.
      if (document.visibilityState === 'visible' && lastSeenHidden) router.refresh();
      lastSeenHidden = document.visibilityState === 'hidden';
    }

    document.addEventListener('visibilitychange', onVisibility);

    const timer = setInterval(() => {
      if (document.visibilityState === 'visible') router.refresh();
    }, seconds * 1000);

    return () => {
      document.removeEventListener('visibilitychange', onVisibility);
      clearInterval(timer);
    };
  }, [seconds, router]);

  return null;
}
