/**
 * HUD：HP/MP/经验条 + 背包 + 装备栏 + 技能栏 + 物品提示。
 *
 * 全部用 DOM/Canvas 画（Phase 0 的结论：WinForms 那 29k 行对话框用 HTML 重做更快更好维护）。
 * 图标来自 Items.Lib / MagIcon.Lib，仍然走 .Lib 的 Range 按需解码。
 */
import { LibStore } from '../formats/lib.ts';
import type { ClientMagicInfo, ItemInfo, UserItem } from '../formats/item.ts';
import { EquipmentSlot } from '../net/enums.ts';
import { formatStats, statName } from '../game/stats.ts';

export interface HudState {
  name: string;
  level: number;
  hp: number;
  maxHp: number;
  mp: number;
  maxMp: number;
  exp: number;
  maxExp: number;
  gold: number;
  inventory: (UserItem | null)[];
  equipment: (UserItem | null)[];
  magics: ClientMagicInfo[];
  stats: Record<number, number>;
}

const EQUIP_LABELS: Record<number, string> = {
  [EquipmentSlot.Weapon]: '武器',
  [EquipmentSlot.Armour]: '衣服',
  [EquipmentSlot.Helmet]: '头盔',
  [EquipmentSlot.Torch]: '蜡烛',
  [EquipmentSlot.Necklace]: '项链',
  [EquipmentSlot.BraceletL]: '手镯',
  [EquipmentSlot.BraceletR]: '手镯',
  [EquipmentSlot.RingL]: '戒指',
  [EquipmentSlot.RingR]: '戒指',
  [EquipmentSlot.Amulet]: '护符',
  [EquipmentSlot.Belt]: '腰带',
  [EquipmentSlot.Boots]: '鞋子',
  [EquipmentSlot.Stone]: '宝石',
  [EquipmentSlot.Mount]: '坐骑',
};

interface SlotView {
  root: HTMLDivElement;
  canvas: HTMLCanvasElement;
  count: HTMLSpanElement;
  dura: HTMLDivElement;
  item: UserItem | null;
  iconKey: string;
}

export class Hud {
  private store: LibStore;
  private host: HTMLElement;

  private hpBar!: HTMLElement;
  private hpText!: HTMLElement;
  private mpBar!: HTMLElement;
  private mpText!: HTMLElement;
  private expBar!: HTMLElement;
  private expText!: HTMLElement;
  private goldText!: HTMLElement;

  private inventoryPanel!: HTMLElement;
  private inventoryGrid!: HTMLElement;
  private equipmentPanel!: HTMLElement;
  private equipmentGrid!: HTMLElement;
  private skillBar!: HTMLElement;
  private tooltip!: HTMLElement;

  private invSlots: SlotView[] = [];
  private equipSlots: SlotView[] = [];
  private skillSlots: SlotView[] = [];
  private lastState?: HudState;

  onEquip?: (item: UserItem) => void;
  onUnequip?: (equipmentSlot: number) => void;
  onUseSkill?: (key: number) => void;

  constructor(store: LibStore, host: HTMLElement) {
    this.store = store;
    this.host = host;
    this.build();
    window.addEventListener('keydown', (ev) => {
      const target = ev.target as HTMLElement | null;
      if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA')) return;
      if (ev.key === 'i' || ev.key === 'I') this.togglePanel(this.inventoryPanel);
      if (ev.key === 'c' || ev.key === 'C') this.togglePanel(this.equipmentPanel);
    });
  }

  // ---------------- DOM ----------------

  private build() {
    const root = document.createElement('div');
    root.id = 'hud-root';
    root.innerHTML = `
      <div id="vitals">
        <div class="vital-row"><span class="vital-label">HP</span><div class="vital-bar hp"><i></i></div><span class="vital-num" data-k="hp"></span></div>
        <div class="vital-row"><span class="vital-label">MP</span><div class="vital-bar mp"><i></i></div><span class="vital-num" data-k="mp"></span></div>
        <div class="vital-row"><span class="vital-label">EXP</span><div class="vital-bar exp"><i></i></div><span class="vital-num" data-k="exp"></span></div>
        <div class="vital-row" id="gold-row"><span class="vital-label">金币</span><span class="vital-num" data-k="gold"></span></div>
      </div>

      <div id="panel-buttons">
        <button type="button" data-panel="inventory">背包 I</button>
        <button type="button" data-panel="equipment">装备 C</button>
      </div>

      <div id="skill-bar" class="panel-win"></div>

      <div id="inventory-panel" class="panel-win hidden">
        <div class="panel-title">背包 <small>双击装备 · I 关闭</small></div>
        <div class="grid" id="inventory-grid"></div>
      </div>

      <div id="equipment-panel" class="panel-win hidden">
        <div class="panel-title">装备 <small>C 关闭</small></div>
        <div class="grid equip" id="equipment-grid"></div>
        <div id="equip-stats"></div>
      </div>

      <div id="item-tooltip" class="hidden"></div>
    `;
    this.host.appendChild(root);

    this.hpBar = root.querySelector('.vital-bar.hp i')!;
    this.hpText = root.querySelector('[data-k="hp"]')!;
    this.mpBar = root.querySelector('.vital-bar.mp i')!;
    this.mpText = root.querySelector('[data-k="mp"]')!;
    this.expBar = root.querySelector('.vital-bar.exp i')!;
    this.expText = root.querySelector('[data-k="exp"]')!;
    this.goldText = root.querySelector('[data-k="gold"]')!;
    this.inventoryPanel = root.querySelector('#inventory-panel')!;
    this.inventoryGrid = root.querySelector('#inventory-grid')!;
    this.equipmentPanel = root.querySelector('#equipment-panel')!;
    this.equipmentGrid = root.querySelector('#equipment-grid')!;
    this.skillBar = root.querySelector('#skill-bar')!;
    this.tooltip = root.querySelector('#item-tooltip')!;

    for (const button of root.querySelectorAll<HTMLButtonElement>('#panel-buttons button')) {
      button.addEventListener('click', () => {
        const panel = button.dataset.panel === 'equipment' ? this.equipmentPanel : this.inventoryPanel;
        this.togglePanel(panel);
      });
    }
    for (let i = 0; i < 14; i++) {
      const slot = this.createSlot();
      const label = document.createElement('span');
      label.className = 'slot-label';
      label.textContent = EQUIP_LABELS[i] ?? '';
      slot.root.appendChild(label);
      slot.root.dataset.equipSlot = String(i);
      this.equipmentGrid.appendChild(slot.root);
      this.equipSlots.push(slot);
      this.bindTooltip(slot, () => this.equipSlots[i].item);
      slot.root.addEventListener('dblclick', () => {
        if (this.equipSlots[i].item) this.onUnequip?.(i);
      });
    }

    for (let i = 0; i < 8; i++) {
      const slot = this.createSlot();
      const key = document.createElement('span');
      key.className = 'slot-key';
      key.textContent = String(i + 1);
      slot.root.appendChild(key);
      slot.root.addEventListener('click', () => this.onUseSkill?.(i + 1));
      this.skillBar.appendChild(slot.root);
      this.skillSlots.push(slot);
    }
  }

  private createSlot(): SlotView {
    const root = document.createElement('div');
    root.className = 'slot';
    const canvas = document.createElement('canvas');
    canvas.width = 40;
    canvas.height = 40;
    const count = document.createElement('span');
    count.className = 'slot-count';
    const dura = document.createElement('div');
    dura.className = 'slot-dura';
    root.append(canvas, count, dura);
    return { root, canvas, count, dura, item: null, iconKey: '' };
  }

  private togglePanel(panel: HTMLElement) {
    panel.classList.toggle('hidden');
    if (this.lastState) this.update(this.lastState);
  }

  private bindTooltip(slot: SlotView, getItem: () => UserItem | null) {
    slot.root.addEventListener('mouseenter', () => {
      const item = getItem();
      if (!item?.info) return;
      this.showTooltip(item, slot.root);
    });
    slot.root.addEventListener('mouseleave', () => this.tooltip.classList.add('hidden'));
  }

  // ---------------- 数据 ----------------

  update(state: HudState) {
    this.lastState = state;

    const hpPct = state.maxHp > 0 ? Math.min(100, (state.hp / state.maxHp) * 100) : 0;
    const mpPct = state.maxMp > 0 ? Math.min(100, (state.mp / state.maxMp) * 100) : 0;
    const expPct = state.maxExp > 0 ? Math.min(100, (state.exp / state.maxExp) * 100) : 0;

    this.hpBar.style.width = `${hpPct}%`;
    this.mpBar.style.width = `${mpPct}%`;
    this.expBar.style.width = `${expPct}%`;
    this.hpText.textContent = `${state.hp} / ${state.maxHp}`;
    this.mpText.textContent = `${state.mp} / ${state.maxMp}`;
    this.expText.textContent = `Lv ${state.level} · ${expPct.toFixed(1)}%`;
    this.goldText.textContent = String(state.gold);

    this.syncInventory(state);
    this.syncEquipment(state);
    this.syncSkills(state);
  }

  private ensureSlots(slots: SlotView[], grid: HTMLElement, count: number, kind: 'inv' | 'equip') {
    while (slots.length < count) {
      const slot = this.createSlot();
      if (kind === 'inv') {
        const index = slots.length;
        slot.root.addEventListener('dblclick', () => {
          const item = this.invSlots[index]?.item;
          if (item) this.onEquip?.(item);
        });
        this.bindTooltip(slot, () => this.invSlots[index]?.item ?? null);
      }
      grid.appendChild(slot.root);
      slots.push(slot);
    }
    while (slots.length > count) {
      const slot = slots.pop()!;
      slot.root.remove();
    }
  }

  private syncInventory(state: HudState) {
    this.ensureSlots(this.invSlots, this.inventoryGrid, state.inventory.length || 40, 'inv');
    this.invSlots.forEach((slot, i) => this.paintSlot(slot, state.inventory[i] ?? null));
  }

  private syncEquipment(state: HudState) {
    this.equipSlots.forEach((slot, i) => this.paintSlot(slot, state.equipment[i] ?? null));

    const statsEl = this.equipmentPanel.querySelector('#equip-stats');
    if (statsEl) {
      const rows = Object.entries(state.stats)
        .filter(([, value]) => value)
        .map(([key, value]) => `<span>${statName(Number(key))} <b>${value}</b></span>`)
        .join('');
      statsEl.innerHTML = rows;
    }
  }

  private syncSkills(state: HudState) {
    this.skillSlots.forEach((slot, i) => {
      const key = i + 1;
      const magic = state.magics.find((m) => m.key === key);
      if (!magic) {
        slot.item = null;
        slot.root.classList.add('empty');
        slot.iconKey = '';
        this.clearCanvas(slot.canvas);
        return;
      }
      slot.root.classList.remove('empty');
      const iconIndex = magic.icon * 2;
      const iconKey = `Data/MagIcon.Lib#${iconIndex}`;
      if (slot.iconKey !== iconKey) {
        slot.iconKey = iconKey;
        void this.drawIcon(slot.canvas, 'Data/MagIcon.Lib', iconIndex);
      }
      void magic;
    });
  }

  private paintSlot(slot: SlotView, item: UserItem | null) {
    slot.item = item;
    if (!item) {
      slot.iconKey = '';
      this.clearCanvas(slot.canvas);
      slot.count.textContent = '';
      slot.dura.style.display = 'none';
      slot.root.classList.add('empty');
      return;
    }

    slot.root.classList.remove('empty');
    const image = item.info?.image ?? -1;
    const iconKey = `Data/Items.Lib#${image}`;
    if (slot.iconKey !== iconKey) {
      slot.iconKey = iconKey;
      if (image >= 0) void this.drawIcon(slot.canvas, 'Data/Items.Lib', image);
      else this.clearCanvas(slot.canvas);
    }

    slot.count.textContent = item.count > 1 ? String(item.count) : '';

    const maxDura = item.maxDura;
    if (maxDura > 0) {
      const pct = Math.max(0, Math.min(1, item.currentDura / maxDura));
      slot.dura.style.display = 'block';
      slot.dura.style.width = `${pct * 100}%`;
      slot.dura.style.background = pct > 0.5 ? '#6ec46e' : pct > 0.2 ? '#d9b445' : '#c4553f';
    } else {
      slot.dura.style.display = 'none';
    }
  }

  private clearCanvas(canvas: HTMLCanvasElement) {
    canvas.getContext('2d')?.clearRect(0, 0, canvas.width, canvas.height);
  }

  private async drawIcon(canvas: HTMLCanvasElement, libPath: string, index: number) {
    const lib = await this.store.get(libPath);
    if (!lib) return;
    const bmp = await lib.image(index);
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    if (!bmp) return;
    const scale = Math.min(canvas.width / bmp.width, canvas.height / bmp.height, 1);
    const w = bmp.width * scale;
    const h = bmp.height * scale;
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(bmp, (canvas.width - w) / 2, (canvas.height - h) / 2, w, h);
  }

  // ---------------- 提示 ----------------

  private showTooltip(item: UserItem, anchor: HTMLElement) {
    const info: ItemInfo | undefined = item.info;
    if (!info) return;

    const gradeColour = ['#e8ded0', '#7ad0ff', '#8bff9c', '#ffd166', '#ff9de2'][info.grade] ?? '#e8ded0';
    const lines: string[] = [];
    lines.push(`<div class="tip-name" style="color:${gradeColour}">${info.name}${item.count > 1 ? ` ×${item.count}` : ''}</div>`);

    const stats = [...formatStats(info.stats), ...formatStats(item.addedStats)];
    if (stats.length) lines.push(`<div class="tip-stats">${stats.join('<br/>')}</div>`);
    if (item.maxDura > 0) lines.push(`<div class="tip-extra">持久 ${item.currentDura}/${item.maxDura}</div>`);
    if (info.weight) lines.push(`<div class="tip-extra">重量 ${info.weight}</div>`);
    if (info.requiredType === 0 && info.requiredAmount) lines.push(`<div class="tip-extra">需要等级 ${info.requiredAmount}</div>`);
    if (info.toolTip) lines.push(`<div class="tip-extra">${info.toolTip}</div>`);

    const rect = anchor.getBoundingClientRect();
    this.tooltip.innerHTML = lines.join('');
    this.tooltip.classList.remove('hidden');
    const tipRect = this.tooltip.getBoundingClientRect();
    const left = Math.min(window.innerWidth - tipRect.width - 8, rect.right + 8);
    const top = Math.max(8, rect.top - 4);
    this.tooltip.style.left = `${left}px`;
    this.tooltip.style.top = `${top}px`;
  }

  get state() {
    return this.lastState;
  }
}
