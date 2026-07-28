import type {Metadata, Viewport} from 'next';
import {NextIntlClientProvider, hasLocale} from 'next-intl';
import {getMessages, setRequestLocale} from 'next-intl/server';
import {notFound} from 'next/navigation';
import type {ReactNode} from 'react';
import {Providers} from '@/components/providers';
import {routing} from '@/i18n/routing';
import {siteUrl} from '@/lib/supabase/config';
import '../globals.css';

export const metadata: Metadata = {
  // Tanpa ini, gambar Open Graph diselesaikan terhadap localhost saat build.
  metadataBase: new URL(siteUrl()),
  title: {default: 'CircuitSync', template: '%s · CircuitSync'},
  description: 'Kelompokkan device kelistrikan Revit jadi circuit, lalu kirim ke antrean.',
  applicationName: 'CircuitSync',
  icons: {
    icon: [
      {url: '/favicon.ico', sizes: '48x48'},
      {url: '/icon-192.png', type: 'image/png', sizes: '192x192'},
      {url: '/icon-512.png', type: 'image/png', sizes: '512x512'}
    ],
    apple: [{url: '/apple-icon.png', sizes: '180x180'}]
  },
  openGraph: {title: 'CircuitSync', images: ['/og.png'], type: 'website'}
};

export const viewport: Viewport = {
  // Warna chrome browser ikut tema, jadi tepi layar tidak berkedip putih di mode gelap.
  themeColor: [
    {media: '(prefers-color-scheme: light)', color: '#f5f5f7'},
    {media: '(prefers-color-scheme: dark)', color: '#161618'}
  ]
};

export function generateStaticParams() {
  return routing.locales.map((locale) => ({locale}));
}

export default async function LocaleLayout({
  children,
  params
}: {
  children: ReactNode;
  params: Promise<{locale: string}>;
}) {
  const {locale} = await params;
  if (!hasLocale(routing.locales, locale)) notFound();

  setRequestLocale(locale);
  const messages = await getMessages();

  return (
    <html lang={locale} suppressHydrationWarning>
      <body className="min-h-dvh">
        <Providers>
          <NextIntlClientProvider locale={locale} messages={messages}>
            {children}
          </NextIntlClientProvider>
        </Providers>
      </body>
    </html>
  );
}
