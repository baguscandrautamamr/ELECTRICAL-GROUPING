import type {ComponentProps, ReactNode} from 'react';

export function cx(...parts: Array<string | false | null | undefined>) {
  return parts.filter(Boolean).join(' ');
}

export function Card({className, children, ...rest}: ComponentProps<'section'>) {
  return (
    <section className={cx('card p-5 sm:p-6', className)} {...rest}>
      {children}
    </section>
  );
}

export function CardHeader({title, hint, action}: {title: string; hint?: string; action?: ReactNode}) {
  return (
    <header className="mb-4 flex items-start justify-between gap-4">
      <div className="min-w-0">
        <h2 className="text-[15px] font-semibold tracking-tight">{title}</h2>
        {hint ? <p className="mt-1 text-[13px] leading-relaxed text-muted">{hint}</p> : null}
      </div>
      {action}
    </header>
  );
}

type ButtonTone = 'primary' | 'secondary' | 'quiet' | 'danger';

const TONES: Record<ButtonTone, string> = {
  primary: 'bg-accent text-accent-ink border-accent hover:opacity-90',
  secondary: 'bg-sunken text-ink border-hairline hover:border-muted',
  quiet: 'bg-transparent text-muted border-transparent hover:text-ink',
  danger: 'bg-transparent text-danger border-transparent hover:bg-danger/10'
};

export function Button({
  tone = 'secondary',
  className,
  ...rest
}: ComponentProps<'button'> & {tone?: ButtonTone}) {
  return (
    <button
      className={cx(
        'inline-flex items-center justify-center gap-2 rounded-[var(--radius-control)] border px-3.5 py-2',
        'text-[13px] font-semibold transition-all duration-200 ease-[var(--ease-soft)]',
        'active:scale-[0.98] disabled:pointer-events-none disabled:opacity-40',
        TONES[tone],
        className
      )}
      {...rest}
    />
  );
}

export function Field({
  label,
  hint,
  className,
  ...rest
}: ComponentProps<'input'> & {label: string; hint?: string}) {
  return (
    <label className="block">
      <span className="text-[11px] font-semibold tracking-wide text-muted uppercase">{label}</span>
      <input
        className={cx(
          'mt-1.5 w-full rounded-[var(--radius-control)] border border-hairline bg-sunken',
          'px-3 py-2 text-[14px] outline-none transition-colors duration-200',
          'placeholder:text-muted focus:border-accent',
          className
        )}
        {...rest}
      />
      {hint ? <span className="mt-1 block text-[12px] text-muted">{hint}</span> : null}
    </label>
  );
}

export function Select({
  label,
  className,
  children,
  ...rest
}: ComponentProps<'select'> & {label: string}) {
  return (
    <label className="block">
      <span className="text-[11px] font-semibold tracking-wide text-muted uppercase">{label}</span>
      <select
        className={cx(
          'mt-1.5 w-full rounded-[var(--radius-control)] border border-hairline bg-sunken',
          'px-3 py-2 text-[14px] outline-none transition-colors duration-200 focus:border-accent',
          className
        )}
        {...rest}
      >
        {children}
      </select>
    </label>
  );
}

type BadgeTone = 'neutral' | 'ok' | 'warn' | 'danger' | 'accent';

const BADGE_TONES: Record<BadgeTone, string> = {
  neutral: 'bg-sunken text-muted',
  ok: 'bg-ok/12 text-ok',
  warn: 'bg-warn/15 text-warn',
  danger: 'bg-danger/12 text-danger',
  accent: 'bg-accent/12 text-accent'
};

export function Badge({tone = 'neutral', children}: {tone?: BadgeTone; children: ReactNode}) {
  return (
    <span
      className={cx(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-semibold',
        BADGE_TONES[tone]
      )}
    >
      {children}
    </span>
  );
}

/**
 * Layar kosong adalah ajakan bertindak, bukan permintaan maaf: judulnya menyebut
 * keadaan, isinya menyebut langkah berikutnya.
 */
export function Empty({title, body, action}: {title: string; body?: string; action?: ReactNode}) {
  return (
    <div className="rounded-[var(--radius-card)] border border-dashed border-hairline px-6 py-10 text-center">
      <p className="text-[14px] font-semibold">{title}</p>
      {body ? <p className="mx-auto mt-2 max-w-md text-[13px] leading-relaxed text-muted">{body}</p> : null}
      {action ? <div className="mt-5 flex justify-center">{action}</div> : null}
    </div>
  );
}

const NOTICE_TONES: Record<BadgeTone, string> = {
  neutral: 'border-hairline text-muted',
  ok: 'border-ok/40 text-ok',
  warn: 'border-warn/50 text-warn',
  danger: 'border-danger/40 text-danger',
  accent: 'border-accent/40 text-accent'
};

export function Notice({tone = 'neutral', children}: {tone?: BadgeTone; children: ReactNode}) {
  return (
    <p
      role="status"
      className={cx(
        'rounded-[var(--radius-control)] border px-3 py-2 text-[13px] leading-relaxed',
        NOTICE_TONES[tone]
      )}
    >
      {children}
    </p>
  );
}
