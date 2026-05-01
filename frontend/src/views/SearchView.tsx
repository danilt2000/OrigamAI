import { useState } from 'react';
import { api, type SearchResponse } from '../api';
import { ResultCard } from '../components/ResultCard';

export function SearchView() {
  const [q, setQ] = useState('');
  const [limit, setLimit] = useState(5);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [res, setRes] = useState<SearchResponse | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!q.trim()) return;
    setLoading(true);
    setErr(null);
    try {
      const r = await api.search(q.trim(), limit);
      setRes(r);
    } catch (e: any) {
      setErr(e.message ?? String(e));
    } finally {
      setLoading(false);
    }
  }

  async function handleDelete(id: string) {
    if (!confirm(`Delete document "${id}"?`)) return;
    try {
      await api.deleteDocument(id);
      setRes(prev => (prev ? { ...prev, results: prev.results.filter(r => r.documentId !== id) } : prev));
    } catch (e: any) {
      alert(e.message ?? String(e));
    }
  }

  return (
    <div className="space-y-4">
      <form onSubmit={submit} className="flex gap-2">
        <input
          value={q}
          onChange={e => setQ(e.target.value)}
          placeholder="Search the knowledge base…"
          className="flex-1 bg-zinc-900 border border-zinc-800 rounded-lg px-3 py-2 text-zinc-100 focus:outline-none focus:border-purple-500"
        />
        <input
          type="number"
          min={1}
          max={50}
          value={limit}
          onChange={e => setLimit(parseInt(e.target.value || '5'))}
          className="w-20 bg-zinc-900 border border-zinc-800 rounded-lg px-3 py-2 text-zinc-100 focus:outline-none focus:border-purple-500"
        />
        <button
          type="submit"
          disabled={loading || !q.trim()}
          className="px-5 py-2 rounded-lg bg-purple-600 hover:bg-purple-500 disabled:opacity-40 font-medium"
        >
          {loading ? 'Searching…' : 'Search'}
        </button>
      </form>

      {err && <div className="rounded-lg border border-red-700 bg-red-950/50 p-3 text-sm text-red-200">{err}</div>}

      {res && (
        <>
          <div className="text-xs text-zinc-500">
            {res.noResult || res.results.length === 0
              ? 'No results.'
              : `${res.results.length} result${res.results.length === 1 ? '' : 's'} for "${res.query}"`}
          </div>
          <div className="space-y-3">
            {res.results.map((r, i) => (
              <ResultCard key={i} result={r} onDelete={handleDelete} />
            ))}
          </div>
        </>
      )}
    </div>
  );
}
