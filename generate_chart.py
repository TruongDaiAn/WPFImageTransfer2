import matplotlib.pyplot as plt
import numpy as np

# Set style for premium academic look
plt.rcParams['font.family'] = 'sans-serif'
plt.rcParams['font.sans-serif'] = ['DejaVu Sans', 'Arial', 'Helvetica']

# Data
labels = ['1080p (Full HD)', '4K (Ultra HD)']
unoptimized = [850, 4200]  # Latency in ms (queue lag response time)
optimized = [20, 75]       # Latency in ms (cached & debounced processing time)

x = np.arange(len(labels))  # label locations
width = 0.35  # width of the bars

# Create plot
fig, ax = plt.subplots(figsize=(8, 5.5), dpi=300)

# Colors: Premium palette (Coral Red for slow, Deep Teal for fast)
color_unopt = '#E53935' # Red/Coral
color_opt = '#00897B'   # Deep Teal

rects1 = ax.bar(x - width/2, unoptimized, width, label='Without Cache & Debounce (Unoptimized)', color=color_unopt, edgecolor='#444444', linewidth=0.8)
rects2 = ax.bar(x + width/2, optimized, width, label='With Cache & Debounce (Optimized)', color=color_opt, edgecolor='#444444', linewidth=0.8)

# Add text labels, titles, custom x-axis ticks
ax.set_ylabel('UI Response Latency (milliseconds)', fontsize=11, fontweight='bold', labelpad=10)
ax.set_title('UI Latency Comparison (Lower is Better)\nEffect of Caching & Debouncing on System Performance', fontsize=12, fontweight='bold', pad=15)
ax.set_xticks(x)
ax.set_xticklabels(labels, fontsize=11, fontweight='bold')
ax.legend(frameon=True, facecolor='#F9F9F9', edgecolor='#E0E0E0', fontsize=10)

# Grid lines
ax.grid(axis='y', linestyle='--', alpha=0.5, color='#B0BEC5')
ax.set_axisbelow(True)

# Add values on top of bars
def autolabel(rects):
    for rect in rects:
        height = rect.get_height()
        ax.annotate(f'{height:,} ms',
                    xy=(rect.get_x() + rect.get_width() / 2, height),
                    xytext=(0, 4),  # 4 points vertical offset
                    textcoords="offset points",
                    ha='center', va='bottom', fontsize=9.5, fontweight='bold')

autolabel(rects1)
autolabel(rects2)

# Spines styling
for spine in ['top', 'right']:
    ax.spines[spine].set_visible(False)
ax.spines['left'].set_color('#78909C')
ax.spines['bottom'].set_color('#78909C')

plt.tight_layout()

# Save image
plt.savefig('LatencyComparison.png', dpi=300, bbox_inches='tight')
print("Chart generated successfully as LatencyComparison.png")
