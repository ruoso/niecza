use STD;

grammar NieczaGrammar is STD;

grammar CgOp is STD {
    rule nibbler { <cgexp> }

    token category:cgexp { <sym> }
    proto token cgexp {*}

    token cgopname { <-[ ' " ( ) { } \[ \] \s ]> + }

    token cgexp:op { <[ ( \[ ]>:s {} <cgopname> [ <cgexp> ]* <[ ) \] ]> }
    token cgexp:name { <cgopname> }
    token cgexp:quote { <?before <[ ' " ]>> {} [ :lang(%*LANG<MAIN>) <quote> ] }
    token cgexp:decint { <decint> }
    token cgexp:p6exp { :lang(%*LANG<MAIN>) '{' ~ '}' <statementlist> }
    token cgexp:bad { <!before <[ ) \] ]> > {}
        [ <?stdstopper> <.panic: "Missing cgop"> ]
        <.panic: "Unparsable cgop">
    }
}

grammar Q is STD::Q { #} {
    method tweak(:$CgOp, *%_) {
        if $CgOp.defined { self.cursor_fresh(NieczaGrammar::CgOp) }
        else { nextwith(self, |%_) }
    }
}

grammar P6 is STD::P6 {
    method unitstart() {
        $*CURLEX{'!sub'} = $*unit.create_sub(
            outer => $*settingref,
            outer_frame => $*niecza_outer_frame,
            class => 'Routine',
            cur_pkg => $*unit.abs_pkg('GLOBAL'),
            name => "mainline",
            run_once => !$*settingref || ?$*niecza_outer_frame ||
                $*settingref.run_once);
        $*CURLEX<!sub>.set_return_pass;
        $*CURLEX{'!sub'}.add_my_name('$_') if !$*settingref;
        $*CURLEX{'!sub'}.set_signature(::GLOBAL::Sig.simple());
        $*unit.set_mainline($*CURLEX<!sub>);

        %*LANG<Q> = ::NieczaGrammar::Q ;
        %*LANG<MAIN> = ::NieczaGrammar::P6 ;

        my $h = self;
        loop (my $C = $*CURLEX<!sub>; $C && $C.unit.name ne 'CORE'; $C.=outer) {
            for $C.lex_names -> $lex {
                $h.check_categorical($lex);
                $h = $h.cursor_fresh(%*LANG<MAIN>);
            }
        }

        self;
    }
}

method p6class () { NieczaGrammar::P6 }
