export function RelevanceBar({ value }: { value: number }) {
  const pct = Math.max(0, Math.min(1, value)) * 100;
  return (
    <div className="flex items-center gap-2 min-w-[120px]">
      <div className="flex-1 h-1.5 bg-zinc-800 rounded overflow-hidden">
        <div
          className="h-full bg-gradient-to-r from-purple-500 to-fuchsia-400"
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="text-xs text-zinc-400 tabular-nums w-10 text-right">
        {value.toFixed(2)}
      </span>
    </div>
  );
}
