/**
 * Parse a date string as UTC. Backend DateTimes are stored as UTC but serialized
 * without a 'Z' suffix (SQLite + EF Core DateTime.Kind = Unspecified). Appending
 * 'Z' when missing ensures JavaScript treats the value as UTC before converting
 * to the user's local timezone.
 */
function parseUtc(iso: string): Date {
  const normalized = iso.endsWith('Z') ? iso : `${iso}Z`;
  return new Date(normalized);
}

export function formatDateTime(iso: string): string {
  try {
    return parseUtc(iso).toLocaleString();
  } catch {
    return iso;
  }
}

export function formatTime(iso: string): string {
  try {
    return parseUtc(iso).toLocaleTimeString();
  } catch {
    return iso;
  }
}

export function formatUptime(seconds: number): string {
  if (seconds < 60) {
    return `${Math.floor(seconds)}s`;
  }

  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);

  if (days > 0) {
    return hours > 0 ? `${days}d ${hours}h` : `${days}d`;
  }

  if (hours > 0) {
    return minutes > 0 ? `${hours}h ${minutes}m` : `${hours}h`;
  }

  const secs = Math.floor(seconds % 60);
  return secs > 0 ? `${minutes}m ${secs}s` : `${minutes}m`;
}
