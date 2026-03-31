import { describe, test, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import {
  GlassCard,
  GlassCardHeader,
  GlassCardTitle,
  GlassCardDescription,
  GlassCardContent,
  GlassCardFooter,
} from './GlassCard';

describe('GlassCard', () => {
  test('renders with glass-card data slot', () => {
    render(<GlassCard data-testid="card">Content</GlassCard>);
    const card = screen.getByTestId('card');
    expect(card).toHaveAttribute('data-slot', 'glass-card');
    expect(card).toHaveTextContent('Content');
  });

  test('applies default size', () => {
    render(<GlassCard data-testid="card">Content</GlassCard>);
    expect(screen.getByTestId('card')).toHaveAttribute('data-size', 'default');
  });

  test('applies sm size', () => {
    render(
      <GlassCard data-testid="card" size="sm">
        Content
      </GlassCard>,
    );
    expect(screen.getByTestId('card')).toHaveAttribute('data-size', 'sm');
  });

  test('applies custom className', () => {
    render(
      <GlassCard data-testid="card" className="custom-class">
        Content
      </GlassCard>,
    );
    expect(screen.getByTestId('card')).toHaveClass('custom-class');
  });

  test('renders all sub-components', () => {
    render(
      <GlassCard>
        <GlassCardHeader data-testid="header">
          <GlassCardTitle data-testid="title">Title</GlassCardTitle>
          <GlassCardDescription data-testid="desc">Description</GlassCardDescription>
        </GlassCardHeader>
        <GlassCardContent data-testid="content">Body</GlassCardContent>
        <GlassCardFooter data-testid="footer">Footer</GlassCardFooter>
      </GlassCard>,
    );

    expect(screen.getByTestId('header')).toHaveAttribute('data-slot', 'glass-card-header');
    expect(screen.getByTestId('title')).toHaveTextContent('Title');
    expect(screen.getByTestId('desc')).toHaveTextContent('Description');
    expect(screen.getByTestId('content')).toHaveTextContent('Body');
    expect(screen.getByTestId('footer')).toHaveTextContent('Footer');
  });
});
