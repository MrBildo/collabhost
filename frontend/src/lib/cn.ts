import { clsx } from 'clsx'
import type { ClassValue } from 'clsx'

function cn(...inputs: ClassValue[]): string {
  return clsx(inputs)
}

export { cn }
