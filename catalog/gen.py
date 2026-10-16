import json, sys

out = sys.argv[1]
paper = json.load(open('fleurs_wer.json'))
codes = dict(Afrikaans='af', Amharic='am', Arabic='ar', Assamese='as', Azerbaijani='az', Belarusian='be', Bulgarian='bg',
             Bengali='bn', Bosnian='bs', Catalan='ca', Chinese='zh', Czech='cs', Welsh='cy', Danish='da', German='de',
             Greek='el', English='en', Spanish='es', Estonian='et', Persian='fa', Finnish='fi', Tagalog='tl', French='fr',
             Galician='gl', Gujarati='gu', Hausa='ha', Hebrew='he', Hindi='hi', Croatian='hr', Hungarian='hu',
             Armenian='hy', Indonesian='id', Icelandic='is', Italian='it', Japanese='ja', Javanese='jw', Georgian='ka',
             Kazakh='kk', Khmer='km', Kannada='kn', Korean='ko', Luxembourgish='lb', Lingala='ln', Lao='lo',
             Lithuanian='lt', Latvian='lv', Maori='mi', Macedonian='mk', Malayalam='ml', Mongolian='mn', Marathi='mr',
             Malay='ms', Maltese='mt', Myanmar='my', Norwegian='no', Nepali='ne', Dutch='nl', Occitan='oc',
             Punjabi='pa', Polish='pl', Pashto='ps', Portuguese='pt', Romanian='ro', Russian='ru', Sindhi='sd',
             Slovak='sk', Slovenian='sl', Shona='sn', Somali='so', Serbian='sr', Swedish='sv', Swahili='sw',
             Tamil='ta', Telugu='te', Tajik='tg', Thai='th', Turkish='tr', Ukrainian='uk', Urdu='ur', Uzbek='uz',
             Vietnamese='vi', Yoruba='yo')
missing = set(paper) - set(codes)
assert not missing, missing

# vocova.app sample (FLEURS, 50 utterances): large-v3, turbo, small with one protocol -> used as ratios against small
voc = {'en': (4.1, 4.7, 6.6), 'es': (2.9, 3.3, 6.5), 'fr': (5.8, 6.7, 14.3), 'de': (4.6, 6.0, 12.8),
       'it': (3.2, 3.9, 8.2), 'pt': (6.6, 7.2, 11.0), 'ja': (6.6, 5.8, 16.4), 'ko': (14.9, 14.5, 21.7),
       'zh': (6.7, 8.0, 19.5), 'hi': (15.7, 22.3, 42.4), 'ar': (14.6, 16.1, 32.7)}
cer = {'zh', 'ja', 'th', 'lo', 'my', 'km'}


def r(x):
    return None if x is None else round(x, 1)


quality = {}
for name, v in paper.items():
    c = codes[name]
    q = {fam: {'err': v[fam], 'src': 'paper'} for fam in ['tiny', 'base', 'small', 'medium', 'large-v2']}
    if c in voc and v['small']:
        l3, tb, sm = voc[c]
        q['large-v3'] = {'err': r(v['small'] * l3 / sm), 'src': 'vocova-scaled'}
        q['large-v3-turbo'] = {'err': r(v['small'] * tb / sm), 'src': 'vocova-scaled'}
    elif v['large-v2'] is not None:
        q['large-v3'] = {'err': r(v['large-v2'] * 0.85), 'src': 'estimate'}
        q['large-v3-turbo'] = {'err': r(v['large-v2']), 'src': 'estimate'}
    if c == 'de':
        q['large-v3-turbo-german'] = {'err': r(q['large-v3-turbo']['err'] * 2.628 / 3.649), 'src': 'primeline-scaled'}
    if c == 'zh':
        # Belle model card, CER vs openai turbo on AISHELL-1/2, WenetSpeech net/meeting, HKUST: mean ratio
        ratios = [3.070 / 8.639, 4.114 / 6.014, 10.230 / 13.507, 13.357 / 20.313, 18.944 / 37.324]
        q['belle-turbo-zh'] = {'err': r(q['large-v3-turbo']['err'] * sum(ratios) / len(ratios)), 'src': 'belle-scaled'}
    if c == 'fr':
        # bofenghuang card, short-form WER vs openai large-v3: in-domain average 5.69/7.69,
        # out-of-domain call-center (Zaion) average 25.04/29.02; the mean of both
        ratio = (5.69 / 7.69 + 25.04 / 29.02) / 2
        q['large-v3-french-distil'] = {'err': r(q['large-v3']['err'] * ratio), 'src': 'bofenghuang-scaled'}
    if c == 'sv':
        # KBLab card, FLEURS WER vs openai: large-v3 5.4/7.8, medium 6.6/12.1
        q['kb-large'] = {'err': r(q['large-v3']['err'] * 5.4 / 7.8), 'src': 'kblab-scaled'}
        q['kb-medium'] = {'err': r(q['medium']['err'] * 6.6 / 12.1), 'src': 'kblab-scaled'}
    quality[c] = {'name': name, 'metric': 'CER' if c in cer else 'WER', **q}

# params (M), runtime overhead in MB on top of the file size, relative compute cost
fams = {'tiny': (39, 200, 1), 'base': (74, 240, 2), 'small': (244, 370, 6), 'medium': (769, 560, 16),
        'large-v2': (1550, 800, 32), 'large-v3': (1550, 800, 32), 'large-v3-turbo': (809, 600, 12),
        'large-v3-turbo-german': (809, 600, 12), 'belle-turbo-zh': (809, 600, 12),
        # large-v3 encoder with 16 instead of 32 decoder layers
        'large-v3-french-distil': (1150, 700, 22), 'kb-large': (1550, 800, 32), 'kb-medium': (769, 560, 16)}
H = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/'
G = 'https://huggingface.co/cstr/whisper-large-v3-turbo-german-ggml/resolve/main/'
tree = {f['path']: f for f in json.load(open('hf_tree.json'))}


def m(id, fam, file, quant, base=H, repo='ggerganov/whisper.cpp', sha=None, size=None, langs='multilingual',
      lic='MIT', extra=None):
    if base == H:
        size, sha = tree[file]['size'], tree[file]['lfs']['oid']
    p, ov, cost = fams[fam]
    d = {'id': id, 'family': fam, 'quantization': quant, 'file': file, 'url': base + file, 'repo': repo,
         'sizeBytes': size, 'sha256': sha, 'paramsM': p, 'estMemoryMB': round(size / 1e6) + ov,
         'relativeCompute': cost, 'languages': langs, 'license': lic}
    if extra:
        d.update(extra)
    return d


german = {'upstream': 'primeline/whisper-large-v3-turbo-german'}


def hf(repo):
    return f'https://huggingface.co/{repo}/resolve/main/'

models = [
    m('tiny', 'tiny', 'ggml-tiny.bin', 'f16'),
    m('base', 'base', 'ggml-base.bin', 'f16', extra={'bundledWithGame': True}),
    m('small-q5', 'small', 'ggml-small-q5_1.bin', 'q5_1'),
    m('small', 'small', 'ggml-small.bin', 'f16'),
    m('medium-q5', 'medium', 'ggml-medium-q5_0.bin', 'q5_0'),
    m('medium', 'medium', 'ggml-medium.bin', 'f16'),
    m('turbo-q5', 'large-v3-turbo', 'ggml-large-v3-turbo-q5_0.bin', 'q5_0', extra={'recommendedDefault': True}),
    m('turbo-q8', 'large-v3-turbo', 'ggml-large-v3-turbo-q8_0.bin', 'q8_0'),
    m('turbo', 'large-v3-turbo', 'ggml-large-v3-turbo.bin', 'f16'),
    m('large-v3-q5', 'large-v3', 'ggml-large-v3-q5_0.bin', 'q5_0'),
    m('large-v3', 'large-v3', 'ggml-large-v3.bin', 'f16'),
    m('turbo-german-q5', 'large-v3-turbo-german', 'ggml-model-q5_0.bin', 'q5_0', base=G,
      repo='cstr/whisper-large-v3-turbo-german-ggml', size=574041195,
      sha='15e92e3db0993c52fffa781513eec9253475331c1be808f8fb409285c9d9d030', langs=['de'], lic='Apache-2.0',
      extra=german),
    m('turbo-german', 'large-v3-turbo-german', 'ggml-model.bin', 'f16', base=G,
      repo='cstr/whisper-large-v3-turbo-german-ggml', size=1624555275,
      sha='6eb2e025198a6cbac7bdb1e86e278f5de002e583aae7bdfcf5183ef8da16decd', langs=['de'], lic='Apache-2.0',
      extra=german),
    # Language-specific models: only ones whose authors publish whisper.cpp files themselves, under an open
    # license, with a benchmark against the OpenAI model they are based on.
    m('turbo-chinese', 'belle-turbo-zh', 'ggml-model.bin', 'f16', base=hf('BELLE-2/Belle-whisper-large-v3-turbo-zh-ggml'),
      repo='BELLE-2/Belle-whisper-large-v3-turbo-zh-ggml', size=1624555275,
      sha='2a3bba5bfdb4d4da3d9949a83b405711727ca1941d4d5810895e077eb3cb4d99', langs=['zh'], lic='Apache-2.0',
      extra={'upstream': 'BELLE-2/Belle-whisper-large-v3-turbo-zh'}),
    m('french-distil-q5', 'large-v3-french-distil', 'ggml-model-q5_0.bin', 'q5_0',
      base=hf('bofenghuang/whisper-large-v3-french-distil-dec16'), repo='bofenghuang/whisper-large-v3-french-distil-dec16',
      size=791369259, sha='a804330f5cd369fb7447ad2935eb7bf5b1d628ba246a067255ac70775df720e4', langs=['fr'], lic='MIT'),
    m('swedish-large-q5', 'kb-large', 'ggml-model-q5_0.bin', 'q5_0', base=hf('KBLab/kb-whisper-large'),
      repo='KBLab/kb-whisper-large', size=1081140203,
      sha='6d2863812d7410322bb7d8647a5c7260761300fa946714c9ed66d22bb30bcb19', langs=['sv'], lic='Apache-2.0'),
    m('swedish-medium-q5', 'kb-medium', 'ggml-model-q5_0.bin', 'q5_0', base=hf('KBLab/kb-whisper-medium'),
      repo='KBLab/kb-whisper-medium', size=539212484,
      sha='7f8762e0ade9e0073674c0d5acae942a0b1ea98add9baa008ee89c94eaba43d0', langs=['sv'], lic='Apache-2.0'),
]
cat = {
    'schemaVersion': 1, 'generated': '2026-10-16',
    'notes': [
        'err = error rate in percent (WER, or CER where metric says so). Lower is better.',
        'Quantized variants (q5/q8) share the quality of their family; whisper.cpp reports the loss as small, it is not measured here.',
        'estMemoryMB and relativeCompute are estimates; the in-game self-test replaces them with measured values.'],
    'sources': {
        'paper': 'Radford et al. 2022, Robust Speech Recognition via Large-Scale Weak Supervision, Table 13 (FLEURS WER), https://arxiv.org/abs/2212.04356',
        'vocova-scaled': 'vocova.app FLEURS benchmark (50 utterances per language; large-v3, turbo and small with one protocol), https://vocova.app/blog/ai-transcription-accuracy-benchmark-2026 ; ratio to small, applied to the paper value of small',
        'estimate': 'No per-language measurement found. large-v3 = large-v2 x 0.85 (OpenAI: 10-20% fewer errors than v2), turbo = large-v2 (OpenAI: turbo similar to large-v2)',
        'belle-scaled': 'BELLE-2 model card: CER of Belle-whisper-large-v3-turbo-zh vs openai turbo on AISHELL-1, AISHELL-2, WenetSpeech net and meeting, HKUST; mean ratio applied to the turbo value, https://huggingface.co/BELLE-2/Belle-whisper-large-v3-turbo-zh',
        'bofenghuang-scaled': 'bofenghuang model card: short-form WER of whisper-large-v3-french-distil-dec16 vs openai large-v3, mean of the in-domain and the call-center (Zaion) averages; applied to the large-v3 value, https://huggingface.co/bofenghuang/whisper-large-v3-french-distil-dec16',
        'kblab-scaled': 'KBLab model card: FLEURS WER of kb-whisper vs openai (large-v3 5.4 vs 7.8, medium 6.6 vs 12.1); applied to the large-v3 / medium values, https://huggingface.co/KBLab/kb-whisper-large',
        'primeline-scaled': 'primeline model card: same German test mix, turbo 3.649 vs turbo-german 2.628 WER (-28%), https://huggingface.co/primeline/whisper-large-v3-turbo-german ; applied to the turbo value',
        'memory': 'whisper.cpp README memory table, https://github.com/ggml-org/whisper.cpp#memory-usage',
        'files': 'Hugging Face API file listings (size, LFS sha256)'},
    'ratingThresholds': {'5': 5, '4': 8, '3': 12, '2': 20, '1': 40},
    'models': models, 'quality': quality}
json.dump(cat, open(out, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
print(len(models), 'models', len(quality), 'languages')
for c in ['de', 'en', 'fr', 'tr', 'pl', 'es']:
    print(c, {k: (v['err'], v['src'][:3]) for k, v in quality[c].items() if isinstance(v, dict)})
