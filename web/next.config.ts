import createNextIntlPlugin from 'next-intl/plugin';
import type {NextConfig} from 'next';

const withNextIntl = createNextIntlPlugin('./i18n/request.ts');

const nextConfig: NextConfig = {
  reactStrictMode: true,
  // Build gagal kalau ada error tipe. Lint dijalankan terpisah lewat `pnpm lint`;
  // Next 16 tidak lagi menjalankannya di dalam build.
  typescript: {ignoreBuildErrors: false}
};

export default withNextIntl(nextConfig);
