'use client';

import {ThemeProvider} from 'next-themes';
import type {ReactNode} from 'react';

/**
 * Terang adalah default, gelap wajib bekerja. `enableSystem` sengaja dimatikan:
 * kalau tema mengikuti sistem, "default terang" jadi janji yang tidak ditepati
 * di mesin yang setelan sistemnya gelap.
 */
export function Providers({children}: {children: ReactNode}) {
  return (
    <ThemeProvider attribute="class" defaultTheme="light" enableSystem={false} disableTransitionOnChange>
      {children}
    </ThemeProvider>
  );
}
