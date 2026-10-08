export type ISODate = string;

export type DateTime = string;

export interface DateRange {
  from: ISODate;
  to: ISODate;
}

export type ViewStatus = 'loading' | 'error' | 'empty' | 'ready';

export interface FilterOption<V extends string = string> {
  value: V;
  label: string;
  count?: number;
}
