import { useState } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { api, type AskResponse } from '../api';

export function AskView() {
  const [q, setQ] = useState('');
  const [filter, setFilter] = useState('');
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [res, setRes] = useState<AskResponse | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!q.trim()) return;
    setLoading(true);
    setErr(null);
    setRes(null);
    try {
      const r = await api.ask(q.trim(), filter.trim() || undefined);
      setRes(r);
    } catch (e: any) {
      setErr(e.message ?? String(e));
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="space-y-4">
      <form onSubmit={submit} className="space-y-3">
        <textarea
          value={q}
          onChange={e => setQ(e.target.value)}
          placeholder="Ask the knowledge base anything…"
          rows={3}
          className="w-full bg-zinc-900 border border-zinc-800 rounded-lg p-3 text-zinc-100 focus:outline-none focus:border-purple-500"
        />
        <div className="flex gap-2">
          <input
            value={filter}
            onChange={e => setFilter(e.target.value)}
            placeholder="Optional tag filter (e.g. source=origam-community)"
            className="flex-1 bg-zinc-900 border border-zinc-800 rounded-lg px-3 py-2 text-sm text-zinc-100 focus:outline-none focus:border-purple-500"
          />
          <button
            type="submit"
            disabled={loading || !q.trim()}
            className="px-5 py-2 rounded-lg bg-purple-600 hover:bg-purple-500 disabled:opacity-40 disabled:cursor-not-allowed font-medium"
          >
            {loading ? 'Asking…' : 'Ask'}
          </button>
        </div>
      </form>

      {err && <div className="rounded-lg border border-red-700 bg-red-950/50 p-3 text-sm text-red-200">{err}</div>}

      {res && (
        <div className="space-y-4">
          <div className="rounded-lg border border-zinc-800 bg-zinc-900/50 p-4">
            <div className="text-xs uppercase tracking-wider text-zinc-500 mb-2">Answer</div>
            <div className="markdown-body">
              <ReactMarkdown remarkPlugins={[remarkGfm]}>{res.answer}</ReactMarkdown>
            </div>
          </div>

          {res.sources.length > 0 && (
            <div className="rounded-lg border border-zinc-800 bg-zinc-900/30 p-4">
              <div className="text-xs uppercase tracking-wider text-zinc-500 mb-2">Sources</div>
              <ul className="space-y-1.5">
                {res.sources.map((s, i) => (
                  <li key={i} className="text-sm flex items-center gap-2">
                    <span className="text-zinc-500 text-xs tabular-nums w-10">{s.relevance.toFixed(2)}</span>
                    {s.url ? (
                      <a href={s.url} target="_blank" rel="noreferrer" className="text-purple-400 hover:text-purple-300">
                        {s.title || s.documentId}
                      </a>
                    ) : (
                      <span>{s.title || s.documentId}</span>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
