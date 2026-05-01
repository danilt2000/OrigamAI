import { useEffect, useState } from 'react';

type Listener = (urls: string[] | null, index: number) => void;
const listeners = new Set<Listener>();

export const lightbox = {
  open(urls: string[], index = 0) {
    listeners.forEach(l => l(urls, index));
  },
  close() {
    listeners.forEach(l => l(null, 0));
  },
};

export function ImageLightbox() {
  const [urls, setUrls] = useState<string[] | null>(null);
  const [index, setIndex] = useState(0);

  useEffect(() => {
    const l: Listener = (u, i) => {
      setUrls(u);
      setIndex(i);
    };
    listeners.add(l);
    return () => {
      listeners.delete(l);
    };
  }, []);

  useEffect(() => {
    if (!urls) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') lightbox.close();
      if (e.key === 'ArrowRight') setIndex(i => Math.min(urls.length - 1, i + 1));
      if (e.key === 'ArrowLeft') setIndex(i => Math.max(0, i - 1));
    };
    document.body.style.overflow = 'hidden';
    window.addEventListener('keydown', onKey);
    return () => {
      document.body.style.overflow = '';
      window.removeEventListener('keydown', onKey);
    };
  }, [urls]);

  if (!urls || urls.length === 0) return null;

  const multi = urls.length > 1;
  const safeIndex = Math.max(0, Math.min(urls.length - 1, index));

  return (
    <div
      className="fixed inset-0 z-50 bg-black/85 backdrop-blur-sm fade-in flex items-center justify-center p-6"
      onClick={() => lightbox.close()}
    >
      <button
        onClick={e => {
          e.stopPropagation();
          lightbox.close();
        }}
        className="absolute top-4 right-4 w-9 h-9 rounded-full bg-zinc-800/80 hover:bg-zinc-700 text-zinc-100 text-lg flex items-center justify-center"
        title="Close (Esc)"
      >
        ×
      </button>

      {multi && (
        <>
          <button
            onClick={e => {
              e.stopPropagation();
              setIndex(i => Math.max(0, i - 1));
            }}
            disabled={safeIndex === 0}
            className="absolute left-4 top-1/2 -translate-y-1/2 w-10 h-10 rounded-full bg-zinc-800/80 hover:bg-zinc-700 disabled:opacity-30 text-zinc-100 text-2xl flex items-center justify-center"
            title="Previous (←)"
          >
            ‹
          </button>
          <button
            onClick={e => {
              e.stopPropagation();
              setIndex(i => Math.min(urls.length - 1, i + 1));
            }}
            disabled={safeIndex === urls.length - 1}
            className="absolute right-4 top-1/2 -translate-y-1/2 w-10 h-10 rounded-full bg-zinc-800/80 hover:bg-zinc-700 disabled:opacity-30 text-zinc-100 text-2xl flex items-center justify-center"
            title="Next (→)"
          >
            ›
          </button>
        </>
      )}

      <img
        src={urls[safeIndex]}
        onClick={e => e.stopPropagation()}
        className="max-w-full max-h-full object-contain rounded-lg shadow-2xl"
      />

      {multi && (
        <div className="absolute bottom-4 left-1/2 -translate-x-1/2 px-3 py-1 rounded-full bg-zinc-800/80 text-xs text-zinc-200 tabular-nums">
          {safeIndex + 1} / {urls.length}
        </div>
      )}
    </div>
  );
}
