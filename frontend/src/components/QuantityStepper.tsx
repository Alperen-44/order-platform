interface QuantityStepperProps {
  value: number;
  max: number;
  onChange: (value: number) => void;
  label: string;
}

/** − 2 + biçiminde adet seçici. Sıfıra inince ürün sepetten çıkar. */
export function QuantityStepper({ value, max, onChange, label }: QuantityStepperProps) {
  const button =
    "grid h-9 w-9 place-items-center text-lg font-semibold text-ink hover:bg-canvas disabled:cursor-not-allowed disabled:text-line";

  return (
    <div role="group" aria-label={label} className="inline-flex items-center rounded-md border border-line bg-surface">
      <button type="button" className={button} onClick={() => onChange(value - 1)} aria-label="Bir azalt">
        −
      </button>
      <span className="min-w-8 text-center font-semibold tabular-nums" aria-live="polite">
        {value}
      </span>
      <button
        type="button"
        className={button}
        onClick={() => onChange(value + 1)}
        disabled={value >= max}
        aria-label="Bir artır"
      >
        +
      </button>
    </div>
  );
}
