import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
type View = 'dashboard' | 'movimientos' | 'presupuestos' | 'metas' | 'deudas' | 'reportes';
interface Tx { name: string; category: string; date: string; amount: number; type: 'income' | 'expense'; icon: string; color: string }
@Component({ selector: 'app-root', imports: [CommonModule, FormsModule], templateUrl: './app.html', styleUrl: './app.scss' })
export class App {
    view = signal<View>('dashboard'); dark = signal(false); menu = signal(false); modal = signal(false); query = signal(''); type = signal('Todos');
    nav = [['dashboard', 'Inicio', '⌂'], ['movimientos', 'Movimientos', '↕'], ['presupuestos', 'Presupuestos', '▣'], ['metas', 'Metas de ahorro', '◎'], ['deudas', 'Deudas', '▤'], ['reportes', 'Reportes', '◫']] as const;
    txs = signal<Tx[]>([{ name: 'Supermercado Jumbo', category: 'Alimentación', date: 'Hoy, 12:30', amount: -58490, type: 'expense', icon: '🛒', color: 'peach' }, { name: 'Sueldo mensual', category: 'Sueldo', date: 'Hoy, 09:15', amount: 1850000, type: 'income', icon: '↙', color: 'mint' }, { name: 'Cuenta de electricidad', category: 'Servicios', date: 'Ayer, 18:42', amount: -47600, type: 'expense', icon: '⚡', color: 'yellow' }, { name: 'Transferencia ahorro', category: 'Ahorro', date: '26 ago, 10:20', amount: -120000, type: 'expense', icon: '◈', color: 'blue' }, { name: 'Uber', category: 'Transporte', date: '25 ago, 22:14', amount: -8950, type: 'expense', icon: '🚕', color: 'purple' }]);
    filtered = computed(() => this.txs().filter(t => (this.type() === 'Todos' || (this.type() === 'Ingresos' && t.type === 'income') || (this.type() === 'Gastos' && t.type === 'expense')) && (t.name + t.category).toLowerCase().includes(this.query().toLowerCase())));
    money(n: number) { return new Intl.NumberFormat('es-CL', { style: 'currency', currency: 'CLP', maximumFractionDigits: 0 }).format(Math.abs(n)) } select(v: string) { this.view.set(v as View); this.menu.set(false) }
    add(name: string, amount: string, kind: string) { const value = Number(amount); if (!name || !value) return; this.txs.update(x => [{ name, category: kind === 'income' ? 'Otros ingresos' : 'Otros', date: 'Ahora', amount: kind === 'income' ? value : -value, type: kind as 'income' | 'expense', icon: kind === 'income' ? '↙' : '↗', color: kind === 'income' ? 'mint' : 'peach' }, ...x]); this.modal.set(false) }
}
