import pytest
import game
import gm


@pytest.mark.parametrize('woke,generous,bad,right,expected', [
    (True, True, [], 3, 'The Guardian'),
    (False, True, [1], 3, 'The Redeemed'),
    (True, True, [2], 3, 'The Loyal One'),
    (True, False, [4], 3, 'The Prepared'),
    (True, False, [5], 2, 'The Survivor'),
    (False, True, [], 3, 'The Guardian'),  # Guardian before Redeemed/Prepared
    (False, True, [2], 3, 'The Redeemed'),
    (True, True, [4, 5], 3, 'The Loyal One'),
    (True, True, [3], 2, 'The Survivor'),
])
def test_priority(woke, generous, bad, right, expected):
    s = game.GameState(items=[], wellness=85, woke_kyle=woke,
        final_kyle_generous=generous, emergencies_with_right_item=right,
        decisions=[{'checkpoint_number': cp, 'verdict': 'bad'} for cp in bad])
    assert game.traveler_archetype(s) == expected
    s.wellness = 84
    assert game.traveler_archetype(s) is None


def test_detail_does_not_choose_type(monkeypatch):
    monkeypatch.setattr(gm, '_ask_llm', lambda *a: {'traveler_type': 'The Evil One', 'traveler_detail': 'You woke Kyle when everyone else left.'})
    result = gm.get_verdict('Moon Citizen', '', 0, 85, [], {}, traveler_type='The Guardian')
    assert result == {'traveler_detail': 'You woke Kyle when everyone else left.'}


@pytest.mark.parametrize('detail', ['He left.', 'You ' + 'went ' * 21, 'You left. You returned.'])
def test_invalid_detail_empty(monkeypatch, detail):
    monkeypatch.setattr(gm, '_ask_llm', lambda *a: {'traveler_detail': detail})
    assert gm.get_verdict('Moon Citizen', '', 0, 85, [], {}, traveler_type='The Guardian') is None


def test_detail_failure_optional(monkeypatch):
    def fail(*args): raise RuntimeError('offline')
    monkeypatch.setattr(gm, '_ask_llm', fail)
    assert gm.get_verdict('Moon Citizen', '', 0, 85, [], {}, traveler_type='The Guardian') is None


def test_session_tracks_moral_verdict_before_pace_penalty():
    from test_game import raw_choice, stub_turn
    state, _ = game.new_game(['alarm clock', 'flashlight', 'red bull'])
    game.choose(state, 1, stub_turn)
    assert state.woke_kyle is False
    assert state.decisions[0]['checkpoint_number'] == 1
    assert state.decisions[0]['verdict'] == 'bad'
    choices = game.score_choices(state, 'escape campus', [raw_choice(9, 0), raw_choice(5, 0)])
    assert choices[0]['wellness_change'] < 0
    assert choices[0]['verdict'] == 'good'


def test_correct_emergencies_not_plain_completions():
    from test_game import stub_turn
    state, _ = game.new_game(['alarm clock', 'flashlight', 'red bull'])
    game.choose(state, 0, stub_turn)  # dorm -> campus
    game.choose(state, 0, stub_turn)  # heat nap
    game.use_item(state, 'alarm clock')
    assert state.emergencies_with_right_item == 1
    game.choose(state, 0, stub_turn)  # blackout
    game.quest_result(state, True)
    assert state.emergencies_with_right_item == 1


def test_below_threshold_never_requests_detail():
    from test_game import state_at, raw_choice, set_pending
    state = state_at('Moon Approach', wellness=84, distance=20000)
    set_pending(state, [raw_choice(5, 0), raw_choice(1, 1)])
    result = game.choose(state, 0, lambda **_: None,
                         lambda **_: pytest.fail('detail must not be requested'))
    assert result['verdict']['traveler_type'] is None
    assert result['verdict']['traveler_detail'] == ''


def test_play_prints_fixed_card_and_optional_detail(capsys):
    import play
    play.show_verdict(dict(ending_title='Moon Citizen', ending='You landed.', final_distance=0,
        final_wellness=85, traveler_type='The Guardian', traveler_blurb=game.TRAVELER_ARCHETYPES['The Guardian'],
        traveler_detail='You woke Kyle.', key_moments=[]))
    output = capsys.readouterr().out
    assert 'You are: The Guardian' in output and 'You woke Kyle.' in output
