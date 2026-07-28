'use client';

import {createBrowserClient} from '@supabase/ssr';
import {SUPABASE_ANON_KEY, SUPABASE_URL} from './config';

/** Klien untuk komponen browser. Selalu memakai session user — tidak ada key rahasia. */
export function createClient() {
  return createBrowserClient(SUPABASE_URL, SUPABASE_ANON_KEY);
}
