'use client';

import {useLocale, useTranslations} from 'next-intl';
import {useRouter} from 'next/navigation';
import {useState} from 'react';
import {Button, Field, Notice} from '@/components/ui';
import {createClient} from '@/lib/supabase/client';

type Feedback = {tone: 'ok' | 'danger'; text: string} | null;

export function LoginForm({callbackFailed}: {callbackFailed: boolean}) {
  const t = useTranslations('auth');
  const locale = useLocale();
  const router = useRouter();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<Feedback>(callbackFailed ? {tone: 'danger', text: t('callbackFailed')} : null);

  /**
   * Pesan Supabase datang dalam bahasa Inggris dan kadang menyebut detail internal.
   * Dipetakan ke pesan sendiri: menjelaskan apa yang terjadi dan apa yang bisa
   * dilakukan, tanpa menyalin exception mentah.
   */
  function explain(message: string): string {
    const lower = message.toLowerCase();
    if (lower.includes('invalid login credentials')) return t('wrongCredentials');
    if (lower.includes('rate limit') || lower.includes('too many')) return t('rateLimited');

    return t('generic');
  }

  async function run(action: () => Promise<{error: {message: string} | null}>, onDone: () => Feedback) {
    setBusy(true);
    setFeedback(null);

    const {error} = await action();
    setBusy(false);
    setFeedback(error ? {tone: 'danger', text: explain(error.message)} : onDone());
  }

  function submit(event: React.FormEvent) {
    event.preventDefault();

    if (email.trim().length === 0) {
      setFeedback({tone: 'danger', text: t('needEmail')});
      return;
    }

    const supabase = createClient();

    if (password.length === 0) {
      setFeedback({tone: 'danger', text: t('needPassword')});
      return;
    }

    void run(
      () => supabase.auth.signInWithPassword({email: email.trim(), password}),
      () => {
        // Middleware yang memutuskan tujuan; refresh cukup untuk memicunya.
        router.replace(`/${locale}/projects`);
        return null;
      }
    );
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Field
        label={t('email')}
        type="email"
        name="email"
        autoComplete="email"
        inputMode="email"
        value={email}
        onChange={(event) => setEmail(event.target.value)}
      />

      <Field
        label={t('password')}
        type="password"
        name="password"
        autoComplete="current-password"
        value={password}
        onChange={(event) => setPassword(event.target.value)}
      />

      {feedback ? <Notice tone={feedback.tone}>{feedback.text}</Notice> : null}

      {/*
        Satu-satunya aksi di halaman ini adalah masuk. Tidak ada tombol daftar dan tidak
        ada tautan masuk lewat email: akun dibuat admin di Supabase Auth, dan undangannya
        dikirim dari sana — bukan dari layar ini. Kontrol yang tidak akan pernah berhasil
        lebih buruk daripada kontrol yang tidak ada.
      */}
      <Button type="submit" tone="primary" disabled={busy}>
        {t('signIn')}
      </Button>
    </form>
  );
}
