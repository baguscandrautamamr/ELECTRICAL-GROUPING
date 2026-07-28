'use client';

import {Plus} from 'lucide-react';
import {useTranslations} from 'next-intl';
import {useRouter} from 'next/navigation';
import {useState, useTransition} from 'react';
import {Button, Field, Notice} from '@/components/ui';
import {createProject} from './actions';

export function NewProjectForm() {
  const t = useTranslations('projects');
  const errors = useTranslations('errors');
  const router = useRouter();

  const [name, setName] = useState('');
  const [message, setMessage] = useState<{tone: 'ok' | 'danger'; text: string} | null>(null);
  const [pending, startTransition] = useTransition();

  function submit(event: React.FormEvent) {
    event.preventDefault();

    startTransition(async () => {
      const result = await createProject(name);

      if (result.ok) {
        setMessage({tone: 'ok', text: t('created', {name: result.name ?? name})});
        setName('');
        router.refresh();
        return;
      }

      setMessage({
        tone: 'danger',
        text: result.reason === 'name' ? t('needName') : errors('unknown')
      });
    });
  }

  return (
    <form onSubmit={submit} className="space-y-3">
      <Field
        label={t('newName')}
        value={name}
        onChange={(event) => setName(event.target.value)}
        placeholder="Gedung A"
      />
      {message ? <Notice tone={message.tone}>{message.text}</Notice> : null}
      <Button type="submit" tone="primary" disabled={pending}>
        <Plus className="size-4" aria-hidden />
        {t('create')}
      </Button>
    </form>
  );
}
