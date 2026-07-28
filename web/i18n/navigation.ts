import {createNavigation} from 'next-intl/navigation';
import {routing} from './routing';

/**
 * Pakai Link dan redirect dari sini, bukan dari next/link — versi ini yang
 * menambahkan prefix locale sendiri.
 */
export const {Link, redirect, usePathname, useRouter, getPathname} = createNavigation(routing);
