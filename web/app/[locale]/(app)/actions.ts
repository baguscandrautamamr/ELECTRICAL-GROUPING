'use server';

import {getLocale} from 'next-intl/server';
import {redirect} from 'next/navigation';
import {createClient} from '@/lib/supabase/server';

export async function signOut() {
  const supabase = await createClient();
  await supabase.auth.signOut();
  redirect(`/${await getLocale()}/login`);
}
