/**
 * Inandu.Grid.Extensions playground front end.
 *
 * A real <inandu-grid serverSide> whose sort / page / filter events are turned into a REST query
 * string (the same shape createInanduGridDataSource uses) and sent to the ASP.NET Core API, which
 * answers with one page via `IReadOnlyList<Product>.ToInanduGrid(...)`.
 */
import 'zone.js'; // change-detection scheduler — must load before bootstrapApplication
import '@angular/compiler'; // enables JIT template compilation for this esbuild bundle
import { Component, signal } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import {
  InanduGridComponent,
  InanduColumnComponent,
  type InanduGridSortCriterion,
} from '@inandu-solutions/grid-angular';

interface ColumnFilterValue {
  text?: string;
  min?: string;
  max?: string;
  from?: string;
  to?: string;
  bool?: string;
  values?: string[];
}

interface Product {
  id: number;
  name: string;
  sku: string;
  category: string;
  price: number;
  stock: number;
  discontinued: boolean;
  status: string;
  createdOn: string;
}

const API = '/api/products';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [InanduGridComponent, InanduColumnComponent],
  template: `
    <header>
      <h1>Inandu.Grid.Extensions — playground</h1>
      <p>
        Every sort, page and filter round-trips to
        <code>GET {{ lastUrl() }}</code> and comes back as one page from
        <code>ToInanduGrid()</code>.
      </p>
    </header>

    <inandu-grid
      serverSide
      filter="true"
      theme="material"
      [data]="rows()"
      [totalItems]="total()"
      [loading]="loading()"
      [error]="error()"
      [paging]="paging"
      (sortChange)="onSort($event)"
      (pageChange)="onPage($event)"
      (filterChange)="onFilter($event)">
      <inandu-column field="id" title="#" type="number" width="80" sortable="true" />
      <inandu-column field="name" title="Product" width="260" sortable="true" filter="true" />
      <inandu-column field="sku" title="SKU" width="130" sortable="true" filter="true" />
      <inandu-column field="category" title="Category" width="150" sortable="true" filter="true" />
      <inandu-column field="price" title="Price" type="number" format="1.2-2" width="120" sortable="true" filter="true" />
      <inandu-column field="stock" title="Stock" type="number" width="110" sortable="true" filter="true" />
      <inandu-column field="status" title="Status" width="130" sortable="true" filter="true" />
      <inandu-column field="createdOn" title="Created" type="date" width="160" sortable="true" filter="true" />
    </inandu-grid>
  `,
  styles: [
    `:host { display:block; max-width:1100px; margin:2rem auto; font-family:system-ui, sans-serif; }
     header { margin-bottom:1rem; }
     h1 { font-size:1.2rem; margin:0 0 .25rem; }
     p { color:#555; font-size:.85rem; margin:0; }
     code { background:#f2f2f2; padding:.05rem .3rem; border-radius:3px; }`,
  ],
})
export class AppComponent {
  readonly rows = signal<Product[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly lastUrl = signal(API);
  readonly paging = { pageSize: 25 };

  private sort: InanduGridSortCriterion[] = [];
  private page = 1;
  private pageSize = 25;
  private query = '';
  private columnFilters: Record<string, ColumnFilterValue> = {};
  private seq = 0;

  constructor() {
    this.load();
  }

  onSort(sort: InanduGridSortCriterion[]): void {
    this.sort = sort ?? [];
    this.page = 1;
    this.load();
  }

  onPage(e: { page: number; pageSize: number }): void {
    this.page = e.page;
    this.pageSize = e.pageSize;
    this.load();
  }

  onFilter(e: { query: string; columnFilters: Record<string, ColumnFilterValue> }): void {
    this.query = e.query ?? '';
    this.columnFilters = e.columnFilters ?? {};
    this.page = 1;
    this.load();
  }

  private buildQueryString(): string {
    const p = new URLSearchParams();
    p.set('page', String(this.page));
    p.set('pageSize', String(this.pageSize));

    if (this.sort.length) {
      p.set('sort', this.sort.map((s) => (s.direction === 'desc' ? `-${s.field}` : s.field)).join(','));
    }
    if (this.query) {
      p.set('q', this.query);
    }

    for (const [field, value] of Object.entries(this.columnFilters)) {
      if (!value) continue;
      if (value.values && value.values.length) p.set(`${field}_in`, value.values.join(','));
      if (value.text) p.set(`${field}_contains`, value.text);
      if (value.min) p.set(`${field}_gte`, value.min);
      if (value.max) p.set(`${field}_lte`, value.max);
      if (value.from) p.set(`${field}_gte`, value.from);
      if (value.to) p.set(`${field}_lte`, value.to);
      if (value.bool === 'true' || value.bool === 'false') p.set(`${field}_eq`, value.bool);
    }

    return p.toString();
  }

  private load(): void {
    const url = `${API}?${this.buildQueryString()}`;
    this.lastUrl.set(url);
    this.loading.set(true);
    this.error.set('');
    const mine = ++this.seq;

    fetch(url)
      .then((res) => (res.ok ? res.json() : Promise.reject(new Error(`HTTP ${res.status}`))))
      .then((body: { data: Product[]; total: number }) => {
        if (mine !== this.seq) return;
        this.rows.set(body.data);
        this.total.set(body.total);
        this.loading.set(false);
      })
      .catch((err: unknown) => {
        if (mine !== this.seq) return;
        this.error.set(err instanceof Error ? err.message : String(err));
        this.loading.set(false);
      });
  }
}

bootstrapApplication(AppComponent).catch((err) => console.error(err));
