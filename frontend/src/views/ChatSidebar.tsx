import { useState } from 'react';
import type { Chat } from '../db';

type Props = {
  chats: Chat[];
  currentId: string | null;
  onSelect: (id: string) => void;
  onNew: () => void;
  onRename: (id: string, title: string) => void;
  onDelete: (id: string) => void;
};

export function ChatSidebar({ chats, currentId, onSelect, onNew, onRename, onDelete }: Props) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draft, setDraft] = useState('');

  function startRename(c: Chat) {
    setEditingId(c.id);
    setDraft(c.title);
  }
  function commitRename() {
    if (editingId && draft.trim()) onRename(editingId, draft.trim());
    setEditingId(null);
  }

  return (
    <aside className="w-64 shrink-0 border-r border-zinc-800 flex flex-col h-full">
      <div className="p-3 border-b border-zinc-800">
        <button
          onClick={onNew}
          className="w-full px-3 py-2 rounded-lg bg-purple-600 hover:bg-purple-500 text-sm font-medium"
        >
          + New chat
        </button>
      </div>
      <ul className="flex-1 overflow-y-auto p-2 space-y-1">
        {chats.length === 0 && (
          <li className="text-xs text-zinc-500 p-2">No chats yet.</li>
        )}
        {chats.map(c => {
          const active = c.id === currentId;
          return (
            <li
              key={c.id}
              className={`group rounded-md px-2 py-1.5 cursor-pointer flex items-center gap-2 ${
                active ? 'bg-purple-500/15 text-purple-100' : 'text-zinc-300 hover:bg-zinc-900'
              }`}
              onClick={() => onSelect(c.id)}
              onDoubleClick={() => startRename(c)}
            >
              {editingId === c.id ? (
                <input
                  autoFocus
                  value={draft}
                  onChange={e => setDraft(e.target.value)}
                  onBlur={commitRename}
                  onKeyDown={e => {
                    if (e.key === 'Enter') commitRename();
                    if (e.key === 'Escape') setEditingId(null);
                  }}
                  className="flex-1 bg-zinc-950 border border-zinc-700 rounded px-2 py-0.5 text-sm"
                />
              ) : (
                <span className="flex-1 truncate text-sm">{c.title}</span>
              )}
              <button
                onClick={e => {
                  e.stopPropagation();
                  if (confirm(`Delete chat "${c.title}"?`)) onDelete(c.id);
                }}
                className="opacity-0 group-hover:opacity-100 text-xs text-zinc-500 hover:text-red-300"
                title="Delete"
              >
                ✕
              </button>
            </li>
          );
        })}
      </ul>
    </aside>
  );
}
