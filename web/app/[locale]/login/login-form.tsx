'use client';

import {useLocale, useTranslations} from 'next-intl';
import {useRouter} from 'next/navigation';
import {useState} from 'react';
import {Button, Field, Notice, cx} from '@/components/ui';
import {createClient} from '@/lib/supabase/client';
import {siteUrl} from '@/lib/supabase/config';

type Mode = 'link' | 'password';
type Feedback = {tone: 'ok' | 'danger'; text: string} | null;

export function LoginForm({callbackFailed}: {callbackFailed: boolean}) {
  const t = useTranslations('auth');
  const locale = useLocale();
  const router = useRouter();

  const [mode, setMode] = useState<Mode>('link');
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

    // Akses lewat undangan, jadi email yang belum diundang ditolak Supabase dengan
    // "Signups not allowed for otp". Itu bukan kesalahan yang bisa diperbaiki user
    // dengan mencoba lagi — yang perlu dilakukan minta admin mengundang, dan itu yang
    // pantas disebut alih-alih pesan umum.
    if (lower.includes('signups not allowed') || lower.includes('signup is disabled')) {
      return t('notInvited');
    }

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

    if (mode === 'link') {
      void run(
        () =>
          supabase.auth.signInWithOtp({
            email: email.trim(),
            options: {
              emailRedirectTo: `${siteUrl()}/auth/callback?next=/${locale}/projects`,
              // Akses hanya lewat undangan admin. Tanpa ini tautan masuk membuat akun
              // untuk email apa pun yang mengetiknya — pendaftaran sendiri lewat pintu
              // belakang, dan tidak ada satu pun layar yang menyebutkannya.
              shouldCreateUser: false
            }
          }),
        () => ({tone: 'ok', text: t('linkSent', {email: email.trim()})})
      );
      return;
    }

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
      <div
        role="tablist"
        aria-label={t('heading')}
        className="flex gap-1 rounded-[var(--radius-control)] bg-sunken p-1"
      >
        {(['link', 'password'] as const).map((candidate) => (
          <button
            key={candidate}
            type="button"
            role="tab"
            aria-selected={mode === candidate}
            onClick={() => {
              setMode(candidate);
              setFeedback(null);
            }}
            className={cx(
              'flex-1 rounded-[7px] px-3 py-1.5 text-[13px] font-semibold',
              'transition-all duration-200 ease-[var(--ease-soft)]',
              mode === candidate ? 'bg-surface text-ink shadow-sm' : 'text-muted hover:text-ink'
            )}
          >
            {candidate === 'link' ? t('modeLink') : t('modePassword')}
          </button>
        ))}
      </div>

      <Field
        label={t('email')}
        type="email"
        name="email"
        autoComplete="email"
        inputMode="email"
        value={email}
        onChange={(event) => setEmail(event.target.value)}
      />

      {mode === 'password' ? (
        <Field
          label={t('password')}
          type="password"
          name="password"
          autoComplete="current-password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
      ) : null}

      {feedback ? <Notice tone={feedback.tone}>{feedback.text}</Notice> : null}

      {/*
        Tidak ada tombol daftar. Akun dibuat admin lewat Supabase Auth, dan satu-satunya
        aksi di halaman ini adalah masuk — kontrol yang tidak akan pernah berhasil lebih
        buruk daripada kontrol yang tidak ada.
      */}
      <Button type="submit" tone="primary" disabled={busy}>
        {mode === 'link' ? t('sendLink') : t('signIn')}
      </Button>
    </form>
  );
}
