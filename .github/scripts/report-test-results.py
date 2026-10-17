"""Expose test counts/native smoke evidence and failures through GitHub annotations.

Test commands retain their own exit status. This always-run reporter does not turn failures into success.
"""
import json
from pathlib import Path
from xml.etree import ElementTree as ET


def annotation(level: str, title: str, message: str) -> None:
    def escape(value: str) -> str:
        return value.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
    print(f'::{level} title={escape(title)}::{escape(message)}')


ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
for report in sorted(Path('tests').glob('*/TestResults/*.trx')):
    root = ET.parse(report).getroot()
    counts = root.find('t:ResultSummary/t:Counters', ns)
    if counts is not None:
        not_run = sum(result.get('outcome') == 'NotExecuted'
                      for result in root.findall('t:Results/t:UnitTestResult', ns))
        annotation('notice', report.parent.parent.name, ', '.join(
            f'{name}={counts.get(name, "0")}'
            for name in ('total', 'executed', 'passed', 'failed')) + f', skipped_or_not_run={not_run}')
    for result in root.findall('t:Results/t:UnitTestResult', ns):
        if result.get('outcome') != 'Failed':
            continue
        message = result.findtext('t:Output/t:ErrorInfo/t:Message', '', ns)
        stack = result.findtext('t:Output/t:ErrorInfo/t:StackTrace', '', ns)
        annotation('error', result.get('testName', 'Test failure'), f'{message}\n{stack}')

smoke = Path('artifacts/appkit-smoke.json')
if smoke.exists():
    report = json.loads(smoke.read_text())
    annotation('notice' if report['passed'] else 'error', 'AppKit API smoke (not desktop acceptance)',
               json.dumps(report, ensure_ascii=True))
