import { useEffect, useState } from 'react'

// Returns `value` delayed by `delayMs` — the debounced value only updates once
// the input has stopped changing for the delay window. Used to keep a
// per-keystroke value (a hand-typed path) from firing a request on every key
// (FE-FORM-04). A timer is a legitimate useEffect (external synchronization).
function useDebounce<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])

  return debounced
}

export { useDebounce }
