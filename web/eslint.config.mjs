import coreWebVitals from 'eslint-config-next/core-web-vitals';
import typescript from 'eslint-config-next/typescript';

// eslint-config-next 16 sudah berbentuk flat config, jadi tidak perlu FlatCompat.
const config = [
  ...coreWebVitals,
  ...typescript,
  {ignores: ['.next/**', 'node_modules/**', 'next-env.d.ts']}
];

export default config;
