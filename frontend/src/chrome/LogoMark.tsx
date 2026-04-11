type LogoMarkProps = {
  size?: number
  className?: string
}

function LogoMark({ size = 24, className }: LogoMarkProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="-180 -180 360 360"
      width={size}
      height={size}
      className={className}
      aria-hidden="true"
    >
      {/* C bracket — the container shape (gray, heavy) */}
      <path
        d="M10,-120 L-120,-120 L-120,120 L10,120"
        fill="none"
        stroke="var(--wm-text-dim)"
        strokeWidth="44"
        strokeLinecap="square"
        strokeLinejoin="miter"
      />

      {/* H — crossbar + right vertical (amber, lighter weight) */}
      <path
        d="M10,0 L140,0 M140,-120 L140,120"
        fill="none"
        stroke="var(--wm-amber)"
        strokeWidth="24"
        strokeLinecap="butt"
      />

      {/* Host node — junction where H exits C */}
      <circle cx="10" cy="0" r="24" fill="var(--wm-amber)" />
    </svg>
  )
}

export { LogoMark }
export type { LogoMarkProps }
