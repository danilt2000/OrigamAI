import { useState } from 'react';
import { AskView } from './views/AskView';
import { SearchView } from './views/SearchView';
import { IngestView } from './views/IngestView';

type Tab = 'ask' | 'search' | 'ingest';

const TABS: { id: Tab; label: string }[] = [
  { id: 'ask', label: 'Ask' },
  { id: 'search', label: 'Search' },
  { id: 'ingest', label: 'Ingest' },
];

export default function App() {
  const [tab, setTab] = useState<Tab>('ask');

  return (
    <div className="min-h-full max-w-5xl mx-auto px-4 py-6 text-left">
      <header className="mb-6">
        <div className="flex items-baseline gap-3">
          <h1 className="text-2xl font-semibold text-zinc-100">OrigamAI</h1>
          <span className="text-sm text-zinc-500">RAG knowledge base</span>
        </div>
        <nav className="mt-4 flex gap-1 border-b border-zinc-800">
          {TABS.map(t => (
            <button
              key={t.id}
              onClick={() => setTab(t.id)}
              className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
                tab === t.id
                  ? 'border-purple-500 text-purple-300'
                  : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
            >
              {t.label}
            </button>
          ))}
        </nav>
      </header>

      <main>
        {tab === 'ask' && <AskView />}
        {tab === 'search' && <SearchView />}
        {tab === 'ingest' && <IngestView />}
      </main>
    </div>
  );
}
