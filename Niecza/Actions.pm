package Niecza::Actions;
use 5.010;
use strict;
use warnings;

use Op;
use Body;
use Unit;

our $AUTOLOAD;
my %carped;
sub AUTOLOAD {
    my ($cl, $M) = @_;
    if ($AUTOLOAD =~ /^Niecza::Actions::(.*)__S_\d\d\d(.*)$/) {
        # TODO: Change CursorBase so this doesn't happen.
        my $m = "$1__S_$2";
        return $cl->$m($M);
    }
    $M->sorry("Action method $AUTOLOAD not yet implemented") unless $carped{$AUTOLOAD}++;
}

sub ws { }
sub vws { }
sub unv { }
sub comment { }
sub comment__S_Sharp { }
sub spacey { }
sub nofun { }
sub curlycheck { }

sub decint { my ($cl, $M) = @_;
    $M->{_ast} = eval $M->Str; # XXX use a real string parser
}

sub integer { my ($cl, $M) = @_;
    $M->{_ast} =
        ($M->{decint} // $M->{octint} // $M->{hexint} // $M->{binint})->{_ast};
}

sub number { my ($cl, $M) = @_;
    my $child = $M->{integer} // $M->{dec_number} // $M->{rad_number};
    $M->{_ast} = $child ? $child->{_ast} :
        ($M->Str eq 'NaN') ? (1e99999/1e99999) : (1e99999);
}

# Value :: Op
sub value { }
sub value__S_number { my ($cl, $M) = @_;
    $M->sorry("Num is NYI");
}

sub value__S_quote { my ($cl, $M) = @_;
    $M->{_ast} = Op::StringLiteral->new(text => $M->{quote}{_ast});
}

sub ident { my ($cl, $M) = @_;
    $M->{_ast} = $M->Str;
}

sub identifier { my ($cl, $M) = @_;
    $M->{_ast} = $M->Str;
}

# Either String Op
sub morename { my ($cl, $M) = @_;
    $M->{_ast} = $M->{identifier} ? $M->{identifier}{_ast} : $M->{EXPR}{_ast};
}

# { dc: Bool, names: [Either String Op] }
sub name { my ($cl, $M) = @_;
    my @names = map { $_->{_ast} } @{ $M->{morename} };
    unshift @names, $M->{identifier}{_ast} if $M->{identifier};
    $M->{_ast} = { dc => !($M->{identifier}), names => \@names };
}

sub longname {} # look at the children yourself

sub stopper { }

# quote :: Op
sub quote {}

sub quote__S_Double_Double { my ($cl, $M) = @_;
    my $str = "";
    for my $n (@{ $M->{nibble}{nibbles} }) {
        if ($n->isa('Str')) {
            $str .= $n->{TEXT};
        } else {
            $M->sorry("Non-literal contents of strings NYI");
        }
    }
    $M->{_ast} = $str;
}

sub nibbler { }

# term :: Op
sub term { }

sub term__S_value { my ($cl, $M) = @_;
    $M->{_ast} = $M->{value}{_ast};
}

sub term__S_identifier { my ($cl, $M) = @_;
    my $id  = $M->{identifier}{_ast};
    my $sal = $M->{args}{_ast};

    if (@$sal > 1) {
        $M->sorry("Slicel lists are NYI");
        return;
    }

    my $args = $sal->[0] // [];

    $M->{_ast} = Op::CallSub->new(
        invocant => Op::Lexical->new(name => '&' . $id),
        positionals => $args);
}

sub term__S_circumfix { my ($cl, $M) = @_;
    $M->{_ast} = $M->{circumfix}{_ast};
}

sub term__S_scope_declarator { my ($cl, $M) = @_;
    $M->{_ast} = $M->{scope_declarator}{_ast};
}

sub term__S_multi_declarator { my ($cl, $M) = @_;
    $M->{_ast} = $M->{multi_declarator}{_ast};
}

sub term__S_package_declarator { my ($cl, $M) = @_;
    $M->{_ast} = $M->{package_declarator}{_ast};
}

sub term__S_routine_declarator { my ($cl, $M) = @_;
    $M->{_ast} = $M->{routine_declarator}{_ast};
}

sub term__S_regex_declarator { my ($cl, $M) = @_;
    $M->{_ast} = $M->{regex_declarator}{_ast};
}

sub term__S_type_declarator { my ($cl, $M) = @_;
    $M->{_ast} = $M->{type_declarator}{_ast};
}

sub term__S_dotty { my ($cl, $M) = @_;
    $M->{_ast} = $M->{dotty}{_ast};
}

sub term__S_capterm { my ($cl, $M) = @_;
    $M->{_ast} = $M->{capterm}{_ast};
}

sub term__S_sigterm { my ($cl, $M) = @_;
    $M->{_ast} = $M->{sigterm}{_ast};
}

sub term__S_statement_prefix { my ($cl, $M) = @_;
    $M->{_ast} = $M->{statement_prefix}{_ast};
}

sub term__S_variable { my ($cl, $M) = @_;
    $M->{_ast} = $M->{variable}{_ast};
}

sub term__S_DotDotDot { my ($cl, $M) = @_;
    $M->{_ast} = Op::Yada->new(kind => '...');
}

sub term__S_BangBangBang { my ($cl, $M) = @_;
    $M->{_ast} = Op::Yada->new(kind => '!!!');
}

sub term__S_QuestionQuestionQuestion { my ($cl, $M) = @_;
    $M->{_ast} = Op::Yada->new(kind => '???');
}

sub voidmark { my ($cl, $M) = @_;
    $M->{_ast} = 1;
}

sub up { my ($cl, $M) = @_;
    $M->{_ast} = length ($M->Str);
}

sub sigil {}
sub sigil__S_Amp {}
sub sigil__S_Dollar {}
sub sigil__S_At {}
sub sigil__S_Percent {}

sub twigil {}
sub twigil__S_Equal {}
sub twigil__S_Bang {}
sub twigil__S_Dot {}
sub twigil__S_Tilde {}
sub twigil__S_Star {}
sub twigil__S_Question {}
sub twigil__S_Caret {}
sub twigil__S_Colon {}

sub terminator {}
sub terminator__S_Thesis {}
sub terminator__S_Semi {}
sub terminator__S_Ket {}
sub terminator__S_Ly {}
sub stdstopper {}
sub unitstopper {}
sub eat_terminator {}

sub termish {}
sub EXPR {}

sub arglist { my ($cl, $M) = @_;
    $M->sorry("Invocant handling is NYI") if $::INVOCANT_IS;
    my $x = $M->{EXPR}{_ast};

    if ($x && $x->isa('Op::SubCall') && $x->splittable_parcel) {
        $M->{_ast} = $x->positionals;
    } else {
        $M->{_ast} = [$x];
    }
}

sub semiarglist { my ($cl, $M) = @_;
    $M->{_ast} = [ map { $_->{_ast} } @{ $M->{arglist} } ];
}

sub args { my ($cl, $M) = @_;
    if ($M->{semiarglist} && $M->{arglist}[0]) {
        $M->sorry("Interaction between semiargs and args is not understood");
        return;
    }

    $M->{_ast} = $M->{semiarglist} ? $M->{semiarglist}{_ast} :
        $M->{arglist}[0] ? [ $M->{arglist}[0]{_ast} ] : undef;
}

sub statement { my ($cl, $M) = @_;
    if ($M->{label} || $M->{statement_mod_cond}[0] || $M->{statement_mod_loop}[0]) {
        $M->sorry("Control is NYI");
        return;
    }

    $M->{_ast} = $M->{EXPR} ? $M->{EXPR}{_ast} : undef;
}

sub statementlist { my ($cl, $M) = @_;
    $M->{_ast} = Op::StatementList->new(children => 
        [ grep { defined $_ } map { $_->{_ast} } @{ $M->{statement} } ]);
}

sub comp_unit { my ($cl, $M) = @_;
    $M->{_ast} = Unit->new(mainline => Body->new(
        name => 'body',
        do => $M->{statementlist}{_ast}));
}

1;
