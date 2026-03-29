import { describe, test, expect } from 'vitest';
import { formatUptime } from './format';

describe('formatUptime', () => {
  test('formats seconds under a minute', () => {
    expect(formatUptime(0)).toBe('0s');
    expect(formatUptime(1)).toBe('1s');
    expect(formatUptime(45)).toBe('45s');
    expect(formatUptime(59.9)).toBe('59s');
  });

  test('formats minutes and seconds', () => {
    expect(formatUptime(60)).toBe('1m');
    expect(formatUptime(90)).toBe('1m 30s');
    expect(formatUptime(322)).toBe('5m 22s');
  });

  test('formats hours and minutes', () => {
    expect(formatUptime(3600)).toBe('1h');
    expect(formatUptime(3660)).toBe('1h 1m');
    expect(formatUptime(19980)).toBe('5h 33m');
  });

  test('formats days and hours', () => {
    expect(formatUptime(86400)).toBe('1d');
    expect(formatUptime(86400 + 3600)).toBe('1d 1h');
    expect(formatUptime(2 * 86400 + 14 * 3600)).toBe('2d 14h');
  });

  test('drops trailing zero units for days', () => {
    expect(formatUptime(86400)).toBe('1d');
    expect(formatUptime(3 * 86400)).toBe('3d');
  });
});
