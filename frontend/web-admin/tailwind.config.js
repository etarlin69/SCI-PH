/**
 * Tokens del sistema visual SCI-PH (mismos valores que el canvas de diseño).
 * @type {import('tailwindcss').Config}
 */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        primary: {
          50: '#EEF4FB',
          100: '#E8F0FA',
          200: '#D6E4F5',
          300: '#9DB8DA',
          600: '#1B4F8A',
          700: '#163F6E',
          800: '#10325A',
          DEFAULT: '#1B4F8A',
        },
        success: { 50: '#E6F4EC', 600: '#1D7348', 700: '#155C39', DEFAULT: '#1D7348' },
        warning: { 50: '#FDF0E1', 500: '#E07B1F', 600: '#C2610C', 700: '#8A4308', DEFAULT: '#E07B1F' },
        danger: { 50: '#FDECEA', 600: '#B42318', 700: '#951C13', 800: '#7A170F', text: '#A1231A', DEFAULT: '#B42318' },
        ink: { DEFAULT: '#0F1B2A', 2: '#24364B', 3: '#3A4A5C', muted: '#5B6878', disabled: '#6B7684' },
        line: { DEFAULT: '#DCE2E8', soft: '#EDF0F3', strong: '#C5CED8' },
        surface: { ground: '#F3F5F7', subtle: '#F7F9FB', hover: '#EEF2F7', pressed: '#E1E7EE' },
        nav: { DEFAULT: '#0D1F33', active: '#1B3553', hover: '#15304D', border: '#1F3A57', text: '#C9D4E0', muted: '#8FA2B8' },
        chart: { blue: '#1B4F8A', teal: '#1C7C7D', sky: '#7FA9D9', violet: '#6B4FA0', orange: '#E07B1F', band: '#E1EAF4' },
      },
      fontFamily: {
        sans: ['"IBM Plex Sans"', 'Arial', 'sans-serif'],
        mono: ['"IBM Plex Mono"', 'ui-monospace', 'monospace'],
      },
      zIndex: {
        popover: '50',
        backdrop: '999',
        modal: '1000',
        toast: '1100',
      },
      keyframes: {
        'toast-in': { from: { opacity: '0', transform: 'translateY(12px)' }, to: { opacity: '1', transform: 'none' } },
        'fade-in': { from: { opacity: '0' }, to: { opacity: '1' } },
        'slide-in-right': { from: { transform: 'translateX(24px)', opacity: '0' }, to: { transform: 'none', opacity: '1' } },
        'slide-in-up': { from: { transform: 'translateY(24px)', opacity: '0' }, to: { transform: 'none', opacity: '1' } },
        'slide-in-left': { from: { transform: 'translateX(-24px)', opacity: '0' }, to: { transform: 'none', opacity: '1' } },
      },
      animation: {
        'toast-in': 'toast-in 200ms ease-out',
        'fade-in': 'fade-in 150ms ease-out',
        'slide-in-right': 'slide-in-right 200ms ease-out',
        'slide-in-up': 'slide-in-up 200ms ease-out',
        'slide-in-left': 'slide-in-left 200ms ease-out',
      },
    },
  },
  plugins: [],
};
