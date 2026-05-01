import { useEffect, useMemo, useRef, useState } from 'react';

export type ComposerSubmit = { text: string; images: Blob[] };

type Attached = { id: string; blob: Blob };

function fileKey(f: Blob): string {
  // For File: name+size+lastModified makes a strong identity. For pasted Blob: size+type+random.
  const ff = f as File;
  if (typeof ff.name === 'string' && typeof ff.lastModified === 'number') {
    return `${ff.name}::${f.size}::${ff.lastModified}`;
  }
  return `${f.type}::${f.size}::${Math.random().toString(36).slice(2)}`;
}

export function Composer({
  onSubmit,
  disabled,
  placeholder = 'Message…  (Enter to send, Shift+Enter for newline)',
}: {
  onSubmit: (v: ComposerSubmit) => void;
  disabled?: boolean;
  placeholder?: string;
}) {
  const [text, setText] = useState('');
  const [attached, setAttached] = useState<Attached[]>([]);
  const fileRef = useRef<HTMLInputElement>(null);
  const taRef = useRef<HTMLTextAreaElement>(null);

  // Derive previews from attached. useMemo + cleanup-on-removal means we only
  // create one ObjectURL per blob, and revoke it when the blob is removed or unmounts.
  const previews = useMemo(() => attached.map(a => ({ id: a.id, url: URL.createObjectURL(a.blob) })), [attached]);
  useEffect(() => {
    return () => previews.forEach(p => URL.revokeObjectURL(p.url));
  }, [previews]);

  useEffect(() => {
    const ta = taRef.current;
    if (!ta) return;
    ta.style.height = 'auto';
    ta.style.height = Math.min(ta.scrollHeight, 220) + 'px';
  }, [text]);

  function addFiles(files: FileList | File[] | null) {
    if (!files) return;
    const incoming: Attached[] = [];
    for (const f of Array.from(files)) {
      if (!f.type.startsWith('image/')) continue;
      incoming.push({ id: fileKey(f), blob: f });
    }
    if (incoming.length === 0) return;
    setAttached(prev => {
      const seen = new Set(prev.map(a => a.id));
      const merged = [...prev];
      for (const a of incoming) {
        if (!seen.has(a.id)) {
          seen.add(a.id);
          merged.push(a);
        }
      }
      return merged;
    });
  }

  function removeAt(id: string) {
    setAttached(prev => prev.filter(a => a.id !== id));
  }

  function send() {
    const trimmed = text.trim();
    if (!trimmed && attached.length === 0) return;
    onSubmit({ text: trimmed, images: attached.map(a => a.blob) });
    setText('');
    setAttached([]);
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault();
      if (!disabled) send();
    }
  }

  function onPaste(e: React.ClipboardEvent<HTMLTextAreaElement>) {
    const items = e.clipboardData?.items;
    if (!items) return;
    const files: File[] = [];
    for (const it of items) {
      if (it.kind === 'file') {
        const f = it.getAsFile();
        if (f && f.type.startsWith('image/')) files.push(f);
      }
    }
    if (files.length) {
      e.preventDefault();
      addFiles(files);
    }
  }

  function onDrop(e: React.DragEvent<HTMLDivElement>) {
    e.preventDefault();
    addFiles(e.dataTransfer.files);
  }

  return (
    <div
      className="border border-zinc-800 bg-zinc-900/50 rounded-xl p-2"
      onDragOver={e => e.preventDefault()}
      onDrop={onDrop}
    >
      {previews.length > 0 && (
        <div className="flex gap-2 flex-wrap p-1 pb-2">
          {previews.map(p => (
            <div key={p.id} className="relative group">
              <img src={p.url} className="h-16 w-16 object-cover rounded-md border border-zinc-700" />
              <button
                onClick={() => removeAt(p.id)}
                className="absolute -top-1.5 -right-1.5 bg-zinc-900 border border-zinc-700 rounded-full w-5 h-5 text-xs text-zinc-300 hover:text-red-300 hover:border-red-500/60"
                title="Remove"
              >
                ×
              </button>
            </div>
          ))}
        </div>
      )}
      <div className="flex items-end gap-2">
        <button
          type="button"
          onClick={() => fileRef.current?.click()}
          className="shrink-0 w-9 h-9 rounded-lg border border-zinc-700 text-zinc-400 hover:text-zinc-100 hover:border-zinc-500 flex items-center justify-center"
          title="Attach images"
        >
          📎
        </button>
        <input
          ref={fileRef}
          type="file"
          accept="image/*"
          multiple
          className="hidden"
          onChange={e => {
            addFiles(e.target.files);
            e.target.value = '';
          }}
        />
        <textarea
          ref={taRef}
          value={text}
          onChange={e => setText(e.target.value)}
          onKeyDown={onKeyDown}
          onPaste={onPaste}
          rows={1}
          placeholder={placeholder}
          className="flex-1 bg-transparent resize-none outline-none text-sm text-zinc-100 placeholder:text-zinc-500 px-2 py-2 min-h-[36px] max-h-[220px]"
        />
        <button
          type="button"
          onClick={send}
          disabled={disabled || (!text.trim() && attached.length === 0)}
          className="shrink-0 px-4 h-9 rounded-lg bg-purple-600 hover:bg-purple-500 disabled:opacity-60 disabled:cursor-not-allowed text-sm font-medium flex items-center justify-center gap-2 min-w-[72px]"
        >
          {disabled ? <span className="spinner" /> : 'Send'}
        </button>
      </div>
    </div>
  );
}
